using System.Diagnostics;
using System.Text.Json;
using BusinessIntelligence.Modules.Platform.Persistence;
using BusinessIntelligence.Modules.Registry.Onrc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace BusinessIntelligence.Modules.Search;

/// <summary>
/// Rebuilds the search tables from the published registry snapshot in one transaction: pages keep reading the previous
/// build until it commits. Reads <c>registry.companies</c>, <c>registrations</c>, <c>company_activities</c>,
/// <c>caen</c> and <c>registration_periods</c>; writes only the <c>search</c> schema.
/// </summary>
public sealed class BuildSearchIndex(AppDbContext database, TimeProvider time)
{
    public const string Tables = "search.companies, search.names, search.registration_numbers, search.counts";

    public async Task<SearchBuild> ExecuteAsync(CancellationToken cancellationToken)
    {
        var snapshot = await database.PublishedDatasets.AsNoTracking()
            .Where(item => item.Kind == ImportOnrcCompanies.Kind)
            .Select(item => (int?)item.SourceFileId)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("No published ONRC snapshot; import the registry first.");

        var clock = Stopwatch.StartNew();
        var report = new Dictionary<string, object> { ["registrySnapshotId"] = snapshot };
        database.Database.SetCommandTimeout(TimeSpan.FromHours(1));
        await database.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)database.Database.GetDbConnection();
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);

        await Execute(connection, "CREATE TEMP TABLE search_current (company_key text PRIMARY KEY, name text NOT NULL, name_key text COLLATE \"C\" NOT NULL) ON COMMIT DROP", cancellationToken);
        report["currentNames"] = await StageCurrentNamesAsync(connection, snapshot, cancellationToken);

        foreach (var table in Tables.Split(", "))
        {
#pragma warning disable EF1002 // Table names are constants of this class.
            await Execute(connection, $"DELETE FROM {table}", cancellationToken);
#pragma warning restore EF1002
        }

        report["companies"] = await Execute(connection, CompaniesSql, cancellationToken, ("snapshot", snapshot));
        report["activitiesOutsideNomenclature"] = await Scalar(connection, """
            SELECT count(*) FROM registry.company_activities a
            WHERE a.snapshot_id = @snapshot AND NOT EXISTS (SELECT 1 FROM registry.caen e WHERE e.version = a.caen_version AND e.code = a.caen_code)
            """, cancellationToken, ("snapshot", snapshot));
        await Execute(connection, """
            INSERT INTO search.names (company_key, name_key, name, current, last_seen)
            SELECT s.company_key, s.name_key, s.name, true, NULL FROM search_current s
            """, cancellationToken);
        report["earlierNames"] = await Execute(connection, EarlierNamesSql, cancellationToken, ("snapshot", snapshot));
        report["registrationNumbers"] = await Execute(connection, RegistrationNumbersSql, cancellationToken, ("snapshot", snapshot));
        report["countRows"] = await Execute(connection, CountsSql, cancellationToken);
        report["seconds"] = Math.Round(clock.Elapsed.TotalSeconds, 1);

        var build = new SearchBuild { RegistrySnapshotId = snapshot, BuiltAt = time.GetUtcNow(), Report = JsonSerializer.Serialize(report) };
        database.SearchBuilds.Add(build);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return build;
    }

    /// <summary>
    /// Folds the current names in .NET (<see cref="SearchKeys.Name"/> uses Unicode normalization, which SQL lacks),
    /// streaming them from a second connection into the build transaction.
    /// </summary>
    private async Task<long> StageCurrentNamesAsync(NpgsqlConnection target, int snapshot, CancellationToken cancellationToken)
    {
        await using var source = new NpgsqlConnection(database.Database.GetConnectionString());
        await source.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT company_key, name FROM registry.companies WHERE snapshot_id = @snapshot AND name IS NOT NULL", source);
        command.CommandTimeout = 0;
        command.Parameters.AddWithValue("snapshot", snapshot);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await using var writer = await target.BeginBinaryImportAsync("COPY search_current (company_key, name, name_key) FROM STDIN (FORMAT BINARY)", cancellationToken);
        long rows = 0;
        while (await reader.ReadAsync(cancellationToken))
        {
            var name = reader.GetString(1);
            if (SearchKeys.Name(name) is not { } key)
            {
                continue;
            }

            await writer.StartRowAsync(cancellationToken);
            await writer.WriteAsync(reader.GetString(0), NpgsqlDbType.Text, cancellationToken);
            await writer.WriteAsync(name, NpgsqlDbType.Text, cancellationToken);
            await writer.WriteAsync(key, NpgsqlDbType.Text, cancellationToken);
            rows++;
        }

        await writer.CompleteAsync(cancellationToken);
        return rows;
    }

    // Every activity adds its section, division and group; a code missing from the nomenclature adds only itself.
    private const string CompaniesSql = """
        WITH tokens AS (
            SELECT a.company_key, array_agg(DISTINCT a.caen_version || ':' || t) AS caen
            FROM registry.company_activities a
            LEFT JOIN registry.caen e ON e.version = a.caen_version AND e.code = a.caen_code
            CROSS JOIN LATERAL unnest(ARRAY[e.section, e.division, e."group", a.caen_code]) AS t
            WHERE a.snapshot_id = @snapshot AND t IS NOT NULL
            GROUP BY a.company_key
        )
        INSERT INTO search.companies (company_key, cui, registration_number, name, name_key, legal_form, lifecycle,
            first_registration_date, county_number, uat_code, locality_code, caen_version, caen)
        SELECT c.company_key, c.cui, c.registration_number, c.name, s.name_key, c.legal_form, c.lifecycle,
            coalesce(c.first_registration_date, c.registration_date), c.county_number, c.uat_code, c.locality_code, c.caen_version,
            coalesce(t.caen, '{}')
        FROM registry.companies c
        LEFT JOIN search_current s ON s.company_key = c.company_key
        LEFT JOIN tokens t ON t.company_key = c.company_key
        WHERE c.snapshot_id = @snapshot
        """;

    // Names the company's current registrations had in earlier snapshots, with the latest spelling of each.
    private static readonly string EarlierNamesSql = $"""
        INSERT INTO search.names (company_key, name_key, name, current, last_seen)
        SELECT DISTINCT ON (k.company_key, k.name_key) k.company_key, k.name_key, k.name, false,
            max(k.valid_to) OVER (PARTITION BY k.company_key, k.name_key)
        FROM (
            SELECT CASE WHEN r.cui IS NOT NULL THEN 'C' || r.cui ELSE 'R' || r.registration_number END AS company_key,
                {SearchKeys.StripCompanyPrefixSql("p.name_key")} AS name_key, p.name, p.valid_to
            FROM registry.registration_periods p
            JOIN registry.registrations r ON r.snapshot_id = @snapshot AND r.registration_number = p.registration_number
            WHERE p.name IS NOT NULL AND p.name_key IS NOT NULL
        ) k
        WHERE k.name_key <> ''
          AND EXISTS (SELECT 1 FROM search.companies c WHERE c.company_key = k.company_key)
          AND NOT EXISTS (SELECT 1 FROM search_current s WHERE s.company_key = k.company_key AND s.name_key = k.name_key)
        ORDER BY k.company_key, k.name_key, k.valid_to DESC
        """;

    // Numbers of the current registrations, and earlier numbers that history ties to a company's CUI (a company that moved county or was renumbered).
    private static readonly string RegistrationNumbersSql = $"""
        INSERT INTO search.registration_numbers (number_key, company_key)
        SELECT {SearchKeys.RegistrationNumberSql("r.registration_number")},
            CASE WHEN r.cui IS NOT NULL THEN 'C' || r.cui ELSE 'R' || r.registration_number END
        FROM registry.registrations r
        WHERE r.snapshot_id = @snapshot
        UNION
        SELECT {SearchKeys.RegistrationNumberSql("p.registration_number")}, 'C' || p.cui
        FROM registry.registration_periods p
        WHERE p.cui IS NOT NULL AND EXISTS (SELECT 1 FROM search.companies c WHERE c.company_key = 'C' || p.cui)
        """;

    // The empty token stands for "any activity", so one pass also counts companies by county and lifecycle alone.
    private const string CountsSql = """
        INSERT INTO search.counts (county_number, lifecycle, caen, companies)
        SELECT CASE WHEN grouping(x.county_number) = 1 THEN 0 ELSE coalesce(x.county_number, -1) END,
               CASE WHEN grouping(x.lifecycle) = 1 THEN '' ELSE x.lifecycle END,
               x.token, count(*)
        FROM (SELECT county_number, lifecycle, unnest(caen || ''::text) AS token FROM search.companies) x
        GROUP BY GROUPING SETS ((x.county_number, x.lifecycle, x.token), (x.county_number, x.token), (x.lifecycle, x.token), (x.token))
        """;

    private static async Task<int> Execute(NpgsqlConnection connection, string sql, CancellationToken cancellationToken, params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 0 };
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<long> Scalar(NpgsqlConnection connection, string sql, CancellationToken cancellationToken, params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 0 };
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
    }
}
