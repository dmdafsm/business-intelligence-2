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
/// Statements without a known CAEN version (2025 mixes Rev.2 and Rev.3) are left out.
/// </summary>
public sealed class BuildFinancialBenchmarks(AppDbContext database, TimeProvider time)
{
    public const int MinimumPopulation = 10;

    public async Task<int> ExecuteAsync(CancellationToken cancellationToken)
    {
        database.Database.SetCommandTimeout(TimeSpan.FromHours(1));
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await database.Database.ExecuteSqlRawAsync("DELETE FROM financials.benchmarks", cancellationToken);
        var builtAt = time.GetUtcNow();
        // The formulas mirror FinancialIndicators.Calculate; a value that is not calculable is NULL and left out of the percentiles.
        var rows = await database.Database.ExecuteSqlInterpolatedAsync($"""
            WITH base AS (
                SELECT s.year, s.caen_version, s.caen_code,
                       CASE WHEN s.employees IS NULL THEN NULL WHEN s.employees = 0 THEN '0' WHEN s.employees < 10 THEN '1-9'
                            WHEN s.employees < 50 THEN '10-49' WHEN s.employees < 250 THEN '50-249' ELSE '250+' END AS band,
                       s.turnover, s.employees, s.equity, s.liabilities, s.fixed_assets + s.current_assets AS assets,
                       CASE WHEN s.net_profit > 0 THEN s.net_profit WHEN s.net_loss > 0 THEN -s.net_loss
                            ELSE coalesce(s.net_profit, s.net_loss) END AS net,
                       p.turnover AS previous
                FROM financials.statements s
                LEFT JOIN financials.statements p ON p.cui = s.cui AND p.year = s.year - 1
                WHERE s.caen_version IS NOT NULL AND s.caen_code IS NOT NULL
            ), measured AS (
                SELECT b.year, b.caen_version, b.caen_code, b.band, i.indicator, i.value
                FROM base b CROSS JOIN LATERAL (VALUES
                    ('turnover_growth', CASE WHEN b.previous > 0 THEN (b.turnover - b.previous) / b.previous END),
                    ('net_margin', CASE WHEN b.turnover > 0 THEN b.net / b.turnover END),
                    ('turnover_per_employee', CASE WHEN b.employees > 0 THEN b.turnover / b.employees END),
                    ('return_on_equity', CASE WHEN b.equity > 0 THEN b.net / b.equity END),
                    ('debt_ratio', CASE WHEN b.assets > 0 THEN b.liabilities / b.assets END)) AS i(indicator, value)
            ), grouped AS (
                SELECT year, caen_version, caen_code, CASE WHEN GROUPING(band) = 1 THEN {FinancialIndicators.AllSizes} ELSE band END AS size_band,
                       indicator, count(*) AS statements, count(value) AS population,
                       percentile_cont(ARRAY[0.1, 0.25, 0.5, 0.75, 0.9]) WITHIN GROUP (ORDER BY value) AS p
                FROM measured
                GROUP BY GROUPING SETS ((year, caen_version, caen_code, indicator, band), (year, caen_version, caen_code, indicator))
            )
            INSERT INTO financials.benchmarks
                (year, caen_version, caen_code, size_band, indicator, statements, population, p10, p25, p50, p75, p90, formula_version, built_at)
            SELECT year, caen_version, caen_code, size_band, indicator, statements, population,
                   CASE WHEN population >= {MinimumPopulation} THEN round(p[1]::numeric, 6) END,
                   CASE WHEN population >= {MinimumPopulation} THEN round(p[2]::numeric, 6) END,
                   CASE WHEN population >= {MinimumPopulation} THEN round(p[3]::numeric, 6) END,
                   CASE WHEN population >= {MinimumPopulation} THEN round(p[4]::numeric, 6) END,
                   CASE WHEN population >= {MinimumPopulation} THEN round(p[5]::numeric, 6) END,
                   {FinancialIndicators.Version}, {builtAt}
            FROM grouped WHERE size_band IS NOT NULL
            """, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return rows;
    }
}
