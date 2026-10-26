using BusinessIntelligence.Modules.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BusinessIntelligence.Modules.Financials;

/// <summary>
/// The distribution of one indicator among the statements of one year with the same CAEN class and version, in one size
/// band or <see cref="FinancialIndicators.AllSizes"/>. Percentiles are null below <see cref="BuildFinancialBenchmarks.MinimumPopulation"/>.
/// </summary>
public sealed class FinancialBenchmark
{
    public short Year { get; set; }
    public short CaenVersion { get; set; }
    public required string CaenCode { get; set; }
    public required string SizeBand { get; set; }
    public required string Indicator { get; set; }

    /// <summary>Statements in the cohort, whether or not the indicator could be calculated for them.</summary>
    public int Statements { get; set; }

    /// <summary>Statements where the indicator could be calculated; the percentiles describe these.</summary>
    public int Population { get; set; }
    public decimal? P10 { get; set; }
    public decimal? P25 { get; set; }
    public decimal? P50 { get; set; }
    public decimal? P75 { get; set; }
    public decimal? P90 { get; set; }
    public required string FormulaVersion { get; set; }
    public DateTimeOffset BuiltAt { get; set; }
}

/// <summary>
/// Rebuilds <c>financials.benchmarks</c> from all statements in one transaction; readers keep the old table until it commits.
/// Statements are grouped by their published CAEN class or, where the source leaves the version open (2025), by the
/// Rev.3 class derived for them (<see cref="StatementClasses"/>); statements without either are left out.
/// </summary>
public sealed class BuildFinancialBenchmarks(AppDbContext database, TimeProvider time)
{
    public const int MinimumPopulation = 10;

    // Declared before BenchmarkSql: static fields initialize in order.
    private static readonly string Version = StatementClasses.VersionSql("s");
    private static readonly string Code = StatementClasses.CodeSql("s");

    // The formulas mirror FinancialIndicators.Calculate; a value that is not calculable is NULL and left out of the percentiles.
    private static readonly string BenchmarkSql = $$"""
            WITH base AS (
                SELECT s.year, {{Version}} AS caen_version, {{Code}} AS caen_code,
                       CASE WHEN s.employees IS NULL THEN NULL WHEN s.employees = 0 THEN '0' WHEN s.employees < 10 THEN '1-9'
                            WHEN s.employees < 50 THEN '10-49' WHEN s.employees < 250 THEN '50-249' ELSE '250+' END AS band,
                       s.turnover, s.employees, s.equity, s.liabilities, s.fixed_assets + s.current_assets AS assets,
                       CASE WHEN s.net_profit > 0 THEN s.net_profit WHEN s.net_loss > 0 THEN -s.net_loss
                            ELSE coalesce(s.net_profit, s.net_loss) END AS net,
                       p.turnover AS previous
                FROM financials.statements s
                LEFT JOIN financials.statements p ON p.cui = s.cui AND p.year = s.year - 1
                WHERE {{Version}} IS NOT NULL AND {{Code}} IS NOT NULL
            ), measured AS (
                SELECT b.year, b.caen_version, b.caen_code, b.band, i.indicator, i.value
                FROM base b CROSS JOIN LATERAL (VALUES
                    ('turnover_growth', CASE WHEN b.previous > 0 THEN (b.turnover - b.previous) / b.previous END),
                    ('net_margin', CASE WHEN b.turnover > 0 THEN b.net / b.turnover END),
                    ('turnover_per_employee', CASE WHEN b.employees > 0 THEN b.turnover / b.employees END),
                    ('return_on_equity', CASE WHEN b.equity > 0 THEN b.net / b.equity END),
                    ('debt_ratio', CASE WHEN b.assets > 0 THEN b.liabilities / b.assets END)) AS i(indicator, value)
            ), grouped AS (
                SELECT year, caen_version, caen_code, CASE WHEN GROUPING(band) = 1 THEN {0} ELSE band END AS size_band,
                       indicator, count(*) AS statements, count(value) AS population,
                       percentile_cont(ARRAY[0.1, 0.25, 0.5, 0.75, 0.9]) WITHIN GROUP (ORDER BY value) AS p
                FROM measured
                GROUP BY GROUPING SETS ((year, caen_version, caen_code, indicator, band), (year, caen_version, caen_code, indicator))
            )
            INSERT INTO financials.benchmarks
                (year, caen_version, caen_code, size_band, indicator, statements, population, p10, p25, p50, p75, p90, formula_version, built_at)
            SELECT year, caen_version, caen_code, size_band, indicator, statements, population,
                   CASE WHEN population >= {1} THEN round(p[1]::numeric, 6) END,
                   CASE WHEN population >= {1} THEN round(p[2]::numeric, 6) END,
                   CASE WHEN population >= {1} THEN round(p[3]::numeric, 6) END,
                   CASE WHEN population >= {1} THEN round(p[4]::numeric, 6) END,
                   CASE WHEN population >= {1} THEN round(p[5]::numeric, 6) END,
                   {2}, {3}
            FROM grouped WHERE size_band IS NOT NULL
            """;

    /// <summary>The rows written, and how many statements of years with an open CAEN version got a class, by method ("none": no class).</summary>
    public sealed record Result(int Rows, IReadOnlyDictionary<string, int> Classes);

    public async Task<Result> ExecuteAsync(CancellationToken cancellationToken)
    {
        database.Database.SetCommandTimeout(TimeSpan.FromHours(1));
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var classes = await ClassifyAsync(cancellationToken);
        await database.Database.ExecuteSqlRawAsync("DELETE FROM financials.benchmarks", cancellationToken);
        var builtAt = time.GetUtcNow();
        var rows = await database.Database.ExecuteSqlRawAsync(BenchmarkSql, [FinancialIndicators.AllSizes, MinimumPopulation, FinancialIndicators.Version, builtAt], cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new Result(rows, classes);
    }

    /// <summary>
    /// Sets <c>class_version</c>, <c>class_code</c> and <c>class_method</c> on statements whose CAEN version the source
    /// leaves open (see <see cref="StatementClasses"/>), from the Rev.3 nomenclature, the INS correspondence and the
    /// company's activities in the published registry snapshot. Statements with a published version are not touched.
    /// </summary>
    private async Task<Dictionary<string, int>> ClassifyAsync(CancellationToken cancellationToken)
    {
        var snapshot = await database.PublishedDatasets.AsNoTracking()
            .Where(item => item.Kind == Registry.Onrc.ImportOnrcCompanies.Kind)
            .Select(item => (int?)item.SourceFileId)
            .FirstOrDefaultAsync(cancellationToken) ?? 0;
        await database.Database.ExecuteSqlRawAsync(
            "UPDATE financials.statements SET class_version = NULL, class_code = NULL, class_method = NULL WHERE caen_version IS NULL AND class_method IS NOT NULL",
            cancellationToken);
        await database.Database.ExecuteSqlRawAsync("""
            WITH open AS (
                SELECT s.cui, s.year, s.caen_code FROM financials.statements s WHERE s.caen_version IS NULL AND s.caen_code IS NOT NULL
            ), candidates AS (
                SELECT o.cui, o.year, o.caen_code AS declared, e.code, false AS via_rev2
                FROM open o JOIN registry.caen e ON e.version = 3 AND e.level = 'class' AND e.code = o.caen_code
                UNION
                SELECT o.cui, o.year, o.caen_code, c.rev3_code, true
                FROM open o JOIN registry.caen_correspondence c ON c.rev2_code = o.caen_code
            ), activities AS (
                SELECT a.company_key, max(a.caen_version) AS latest_version, array_agg(a.caen_code) FILTER (WHERE a.caen_version = 3) AS rev3
                FROM registry.company_activities a WHERE a.snapshot_id = {0}
                GROUP BY a.company_key
            ), decided AS (
                SELECT c.cui, c.year, c.declared,
                       array_agg(DISTINCT c.code) AS codes,
                       array_agg(DISTINCT c.code) FILTER (WHERE c.code = ANY(a.rev3)) AS registered,
                       array_agg(DISTINCT c.code) FILTER (WHERE c.via_rev2) AS from_rev2,
                       max(a.latest_version) AS registry_version
                FROM candidates c LEFT JOIN activities a ON a.company_key = 'C' || c.cui
                GROUP BY c.cui, c.year, c.declared
            ), chosen AS (
                SELECT cui, year,
                       CASE WHEN cardinality(codes) = 1 THEN codes[1]
                            WHEN cardinality(registered) = 1 THEN registered[1]
                            WHEN registry_version = 2 AND cardinality(from_rev2) = 1 THEN from_rev2[1]
                            WHEN declared = ANY(registered) AND declared = ANY(from_rev2) THEN declared END AS code,
                       CASE WHEN cardinality(codes) = 1 THEN {1}
                            WHEN cardinality(registered) = 1 THEN {2}
                            WHEN registry_version = 2 AND cardinality(from_rev2) = 1 THEN {3}
                            WHEN declared = ANY(registered) AND declared = ANY(from_rev2) THEN {4} END AS method
                FROM decided
            )
            UPDATE financials.statements s SET class_version = 3, class_code = c.code, class_method = c.method
            FROM chosen c
            WHERE c.code IS NOT NULL AND s.cui = c.cui AND s.year = c.year
            """, [snapshot, StatementClasses.OnlyCandidate, StatementClasses.Registry, StatementClasses.RegistryRev2, StatementClasses.SameCode], cancellationToken);
        var counts = await database.FinancialStatements.AsNoTracking()
            .Where(item => item.CaenVersion == null && item.CaenCode != null)
            .GroupBy(item => item.ClassMethod ?? "none")
            .Select(group => new { group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        return counts.ToDictionary(item => item.Key, item => item.Count);
    }
}
