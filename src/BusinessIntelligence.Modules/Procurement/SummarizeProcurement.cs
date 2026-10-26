using BusinessIntelligence.Modules.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BusinessIntelligence.Modules.Procurement;

/// <summary>
/// Rebuilds <c>procurement.year_totals</c> from all imported awards in one transaction (readers keep the old totals until it
/// commits). Awards imported before <c>awarded_on</c> existed get it filled first.
/// </summary>
public sealed class SummarizeProcurement(AppDbContext database)
{
    public async Task<int> ExecuteAsync(CancellationToken cancellationToken)
    {
        database.Database.SetCommandTimeout(TimeSpan.FromHours(1));
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await database.Database.ExecuteSqlRawAsync("""
            UPDATE procurement.awards SET awarded_on = coalesce(contract_date, published_on)
            WHERE awarded_on IS NULL AND (contract_date IS NOT NULL OR published_on IS NOT NULL)
            """, cancellationToken);
        await database.Database.ExecuteSqlRawAsync("DELETE FROM procurement.year_totals", cancellationToken);
        var rows = await database.Database.ExecuteSqlRawAsync("""
            WITH won AS (
                SELECT kind, winner_cui AS cui, min(year) AS year, max(value_ron) AS value
                FROM procurement.awards WHERE winner_cui IS NOT NULL
                GROUP BY kind, authority_cui, authority_name, reference, lot, contract_number, winner_cui
            ), bought AS (
                -- As authority a contract counts once, whatever its winners.
                SELECT kind, authority_cui AS cui, min(year) AS year, max(value_ron) AS value
                FROM procurement.awards WHERE authority_cui IS NOT NULL
                GROUP BY kind, authority_cui, authority_name, reference, lot, contract_number
            ), roles AS (
                SELECT cui, 'W' AS role, kind, year, value FROM won
                UNION ALL
                SELECT cui, 'A', kind, year, value FROM bought
            )
            INSERT INTO procurement.year_totals
                (cui, role, year, contracts, contract_value, contracts_without_value, direct_purchases, direct_value, direct_without_value)
            SELECT cui, role, year,
                   count(*) FILTER (WHERE kind = 'contract'), sum(value) FILTER (WHERE kind = 'contract'),
                   count(*) FILTER (WHERE kind = 'contract' AND value IS NULL),
                   count(*) FILTER (WHERE kind = 'direct'), sum(value) FILTER (WHERE kind = 'direct'),
                   count(*) FILTER (WHERE kind = 'direct' AND value IS NULL)
            FROM roles
            GROUP BY cui, role, year
            """, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return rows;
    }
}
