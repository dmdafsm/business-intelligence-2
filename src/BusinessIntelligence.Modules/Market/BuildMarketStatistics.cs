using BusinessIntelligence.Modules.Platform.Persistence;
using BusinessIntelligence.Modules.Registry.Onrc;
using Microsoft.EntityFrameworkCore;

namespace BusinessIntelligence.Modules.Market;

/// <summary>
/// Rebuilds the <c>market</c> tables from the published ONRC snapshot and the registry history in one transaction; the
/// page keeps reading the previous build until it commits. Reads <c>registry</c> only.
/// </summary>
public sealed class BuildMarketStatistics(AppDbContext database, TimeProvider time)
{
    public async Task<MarketBuild> ExecuteAsync(CancellationToken cancellationToken)
    {
        var published = await database.PublishedDatasets.AsNoTracking()
            .Where(item => item.Kind == ImportOnrcCompanies.Kind)
            .Join(database.SourceFiles, item => item.SourceFileId, file => file.Id, (item, file) => new { file.Id, file.ReferenceDate })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("No published ONRC snapshot; import the registry first.");

        database.Database.SetCommandTimeout(TimeSpan.FromHours(1));
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await database.Database.ExecuteSqlRawAsync("DELETE FROM market.registrations", cancellationToken);
        await database.Database.ExecuteSqlRawAsync("DELETE FROM market.lifecycle_changes", cancellationToken);
        await database.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO market.registrations (month, county_number, form_category, lifecycle, companies)
            SELECT date_trunc('month', first_registration_date)::date, coalesce(county_number, 0), form_category, lifecycle, count(*)
            FROM registry.companies
            WHERE snapshot_id = {published.Id} AND first_registration_date IS NOT NULL
            GROUP BY 1, 2, 3, 4
            """, cancellationToken);
        // A period starts at the snapshot where a registration's row changed; only a changed state counts here. The
        // previous snapshot bounds when the change happened.
        await database.Database.ExecuteSqlInterpolatedAsync($"""
            WITH snapshots AS (
                SELECT snapshot_date, lag(snapshot_date) OVER (ORDER BY snapshot_date) AS previous
                FROM registry.history_snapshots
            ), periods AS (
                SELECT registration_number, valid_from, county_number, lifecycle,
                       lag(lifecycle) OVER (PARTITION BY registration_number ORDER BY valid_from) AS previous_lifecycle
                FROM registry.registration_periods
            )
            INSERT INTO market.lifecycle_changes (observed_on, previous_snapshot, county_number, form_category, from_lifecycle, to_lifecycle, registrations)
            SELECT p.valid_from, s.previous, coalesce(p.county_number, 0), coalesce(r.form_category, 'other'), p.previous_lifecycle, p.lifecycle, count(*)
            FROM periods p
            JOIN snapshots s ON s.snapshot_date = p.valid_from AND s.previous IS NOT NULL
            LEFT JOIN registry.registrations r ON r.snapshot_id = {published.Id} AND r.registration_number = p.registration_number
            WHERE p.previous_lifecycle IS NOT NULL AND p.previous_lifecycle <> p.lifecycle
            GROUP BY 1, 2, 3, 4, 5, 6
            """, cancellationToken);
        var build = new MarketBuild
        {
            RegistrySnapshotId = published.Id,
            ReferenceDate = published.ReferenceDate,
            BuiltAt = time.GetUtcNow(),
        };
        database.MarketBuilds.Add(build);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return build;
    }
}
