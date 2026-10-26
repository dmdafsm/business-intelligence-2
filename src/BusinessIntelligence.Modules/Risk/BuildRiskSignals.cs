using BusinessIntelligence.Modules.Financials;
using BusinessIntelligence.Modules.Platform.Persistence;
using BusinessIntelligence.Modules.Registry.Onrc;
using Microsoft.EntityFrameworkCore;

namespace BusinessIntelligence.Modules.Risk;

/// <summary>
/// Rebuilds <c>risk.signals</c> in one transaction from the published registry snapshot, the latest ANAF answers, the
/// financial statements and the S10 benchmarks; pages keep the previous signals until it commits. Struck-off companies
/// and registrations without a CUI get no signals. A blank value never raises one.
/// </summary>
public sealed class BuildRiskSignals(AppDbContext database, TimeProvider time)
{
    public async Task<RiskBuild> ExecuteAsync(CancellationToken cancellationToken)
    {
        var snapshot = await database.PublishedDatasets.AsNoTracking()
            .Where(item => item.Kind == ImportOnrcCompanies.Kind)
            .Select(item => (int?)item.SourceFileId)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("No published ONRC snapshot; import the registry first.");
        var financialYear = await database.FinancialStatements.MaxAsync(item => (short?)item.Year, cancellationToken);
        var benchmarkYear = await database.FinancialBenchmarks.MaxAsync(item => (short?)item.Year, cancellationToken);

        database.Database.SetCommandTimeout(TimeSpan.FromHours(1));
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await database.Database.ExecuteSqlRawAsync("DELETE FROM risk.signals", cancellationToken);
        await database.Database.ExecuteSqlRawAsync("""
            CREATE TEMP TABLE risk_companies ON COMMIT DROP AS
            SELECT company_key, cui, lifecycle, form_category, first_registration_date
            FROM registry.companies
            WHERE snapshot_id = {0} AND cui IS NOT NULL AND lifecycle <> {1}
            """, [snapshot, Lifecycle.StruckOff], cancellationToken);
        await database.Database.ExecuteSqlRawAsync("CREATE INDEX ON risk_companies (cui)", cancellationToken);

        await database.Database.ExecuteSqlRawAsync("""
            INSERT INTO risk.signals (company_key, cui, signal, since, checked_on)
            SELECT c.company_key, c.cui, {0}, a.inactive_since, a.checked_on
            FROM risk_companies c
            JOIN LATERAL (SELECT * FROM anaf.statuses s WHERE s.cui = c.cui ORDER BY s.checked_on DESC LIMIT 1) a ON true
            WHERE a.found AND a.inactive IS TRUE
            """, [RiskSignals.AnafInactive], cancellationToken);

        if (financialYear is { } year)
        {
            // A company registered before the year was due to file for it; PFA, II and IF do not file these statements.
            await database.Database.ExecuteSqlRawAsync("""
                INSERT INTO risk.signals (company_key, cui, signal, year)
                SELECT c.company_key, c.cui, {0}, {1}::smallint
                FROM risk_companies c
                WHERE c.lifecycle = {2} AND c.form_category = 'company' AND c.first_registration_date < make_date({1}, 1, 1)
                  AND NOT EXISTS (SELECT 1 FROM financials.statements s WHERE s.cui = c.cui AND s.year IN ({1}, {1} - 1))
                """, [RiskSignals.NoRecentStatement, year, Lifecycle.Active], cancellationToken);
            await database.Database.ExecuteSqlRawAsync("""
                INSERT INTO risk.signals (company_key, cui, signal, year, amount)
                SELECT c.company_key, c.cui, {0}, s.year, s.equity
                FROM risk_companies c JOIN financials.statements s ON s.cui = c.cui AND s.year = {1}
                WHERE s.equity < 0
                """, [RiskSignals.NegativeEquity, year], cancellationToken);
            // A loss year: a published loss and no published profit (the net result of FinancialIndicators.NetResult below zero).
            await database.Database.ExecuteSqlRawAsync("""
                INSERT INTO risk.signals (company_key, cui, signal, year, amount)
                SELECT c.company_key, c.cui, {0}, {1}::smallint, -max(s.net_loss) FILTER (WHERE s.year = {1})
                FROM risk_companies c JOIN financials.statements s ON s.cui = c.cui AND s.year BETWEEN {1} - 2 AND {1}
                WHERE s.net_loss > 0 AND coalesce(s.net_profit, 0) <= 0
                GROUP BY c.company_key, c.cui
                HAVING count(*) = 3
                """, [RiskSignals.RepeatedLosses, year], cancellationToken);
        }

        if (benchmarkYear is { } compared)
        {
            // The same debt ratio as FinancialIndicators.DebtRatio, against the all-sizes P90 of the statement's comparison class
            // that year (StatementClasses: published, or derived for 2025).
            await database.Database.ExecuteSqlRawAsync("""
                INSERT INTO risk.signals (company_key, cui, signal, year, ratio, threshold, caen_code)
                SELECT c.company_key, c.cui, {0}, s.year, round(s.liabilities / (s.fixed_assets + s.current_assets), 6), b.p90, b.caen_code
                FROM risk_companies c
                JOIN financials.statements s ON s.cui = c.cui AND s.year = {1}
                JOIN financials.benchmarks b ON b.year = s.year
                    AND b.caen_version = coalesce(s.caen_version, s.class_version)
                    AND b.caen_code = CASE WHEN s.caen_version IS NOT NULL THEN s.caen_code ELSE s.class_code END
                    AND b.size_band = {2} AND b.indicator = {3} AND b.p90 IS NOT NULL
                WHERE s.fixed_assets + s.current_assets > 0 AND s.liabilities / (s.fixed_assets + s.current_assets) > b.p90
                """, [RiskSignals.HighDebtRatio, compared, FinancialIndicators.AllSizes, FinancialIndicators.DebtRatio.Key], cancellationToken);
        }

        var build = new RiskBuild { FinancialYear = financialYear, BenchmarkYear = benchmarkYear, Version = RiskSignals.Version, BuiltAt = time.GetUtcNow() };
        database.RiskBuilds.Add(build);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return build;
    }
}
