using System.Text.Json;
using BusinessIntelligence.Modules.Platform.Persistence;
using BusinessIntelligence.Modules.Registry.Geography;
using BusinessIntelligence.Modules.Registry.Sources;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace BusinessIntelligence.Modules.Registry.Onrc;

/// <summary>
/// Imports an OD_FIRME snapshot in one transaction: stream rows into a temporary table, check the file hash, merge rows per
/// registry number, resolve SIRUTA geography, group registrations into companies by CUI, then switch the published
/// snapshot and delete the previous one. Readers keep seeing the previous snapshot until the commit.
/// </summary>
public sealed class ImportOnrcFirme(AppDbContext database, TimeProvider time)
{
    public const string Kind = "onrc-firme";

    public async Task<ImportResult> ExecuteAsync(string path, CatalogFile file, CancellationToken cancellationToken)
    {
        if (file.Kind != Kind)
        {
            throw new ArgumentException($"Catalog entry {file.Id} is not an OD_FIRME file.", nameof(file));
        }

        var existing = await database.SourceFiles.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Sha256 == file.Sha256 && item.ParserVersion == OnrcFirmeReader.ParserVersion, cancellationToken);
        if (existing is not null)
        {
            return new ImportResult(ImportOutcome.AlreadyImported, existing.Id, new Dictionary<string, object>());
        }

        var places = await database.Siruta.AsNoTracking().ToListAsync(cancellationToken);
        if (places.Count == 0)
        {
            throw new InvalidOperationException("Import SIRUTA before the registry: geography cannot be resolved without it.");
        }

        database.Database.SetCommandTimeout(TimeSpan.FromHours(1));
        await database.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)database.Database.GetDbConnection();
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);

        var source = RegistryImports.NewSourceFile(file, OnrcFirmeReader.ParserVersion, time, "{}");
        database.SourceFiles.Add(source);
        await database.SaveChangesAsync(cancellationToken);

        await ExecuteAsync(connection, Sql.CreateStaging, cancellationToken);
        var counts = new FirmeReadCounts();
        await using (var stream = VerifiedFileStream.Open(path, file))
        {
            await CopyRowsAsync(connection, OnrcFirmeReader.ReadAsync(stream, counts, cancellationToken), cancellationToken);
            stream.EnsureComplete();
        }

        var geography = await ResolveGeographyAsync(connection, new SirutaSeatMatcher(places), cancellationToken);
        var registrations = await ExecuteAsync(connection, Sql.InsertRegistrations, cancellationToken, ("snapshot", source.Id));
        var companies = await ExecuteAsync(connection, Sql.InsertCompanies, cancellationToken, ("snapshot", source.Id));
        var previous = await RegistryImports.PublishAsync(database, Kind, source.Id, time, cancellationToken);
        if (previous is { } old && old != source.Id)
        {
            await ExecuteAsync(connection, "DELETE FROM registry.company_activities WHERE snapshot_id = @snapshot", cancellationToken, ("snapshot", old));
            await ExecuteAsync(connection, "DELETE FROM registry.representatives WHERE snapshot_id = @snapshot", cancellationToken, ("snapshot", old));
            await ExecuteAsync(connection, "DELETE FROM registry.registration_statuses WHERE snapshot_id = @snapshot", cancellationToken, ("snapshot", old));
            await ExecuteAsync(connection, "DELETE FROM registry.companies WHERE snapshot_id = @snapshot", cancellationToken, ("snapshot", old));
            await ExecuteAsync(connection, "DELETE FROM registry.registrations WHERE snapshot_id = @snapshot", cancellationToken, ("snapshot", old));
        }

        var report = new Dictionary<string, object>
        {
            ["rows"] = counts.Rows,
            ["rowsWithoutRegistrationNumber"] = counts.WithoutRegistrationNumber,
            ["invalidRegistrationDates"] = counts.InvalidRegistrationDate,
            ["cuiStates"] = counts.CuiStates,
            ["distinctCountyLocalityPairs"] = geography.Pairs,
            ["geographyStatesByPair"] = geography.States,
            ["registrations"] = registrations,
            ["companies"] = companies,
            ["registrationsWithConflicts"] = await ScalarAsync(connection,
                "SELECT count(*) FROM registry.registrations WHERE snapshot_id = @snapshot AND cardinality(conflicting_fields) > 0", source.Id, cancellationToken),
        };
        source.Report = JsonSerializer.Serialize(report);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await RegistryImports.VacuumAsync(database,
            "registry.registrations, registry.companies, registry.registration_statuses, registry.company_activities", cancellationToken);
        return new ImportResult(ImportOutcome.Published, source.Id, report);
    }

    private static async Task CopyRowsAsync(NpgsqlConnection connection, IAsyncEnumerable<FirmeRow> rows, CancellationToken cancellationToken)
    {
        await using var writer = await connection.BeginBinaryImportAsync(Sql.CopyStaging, cancellationToken);
        await foreach (var row in rows)
        {
            await writer.StartRowAsync(cancellationToken);
            await writer.WriteAsync(row.RowNumber, NpgsqlDbType.Integer, cancellationToken);
            await writer.WriteAsync(row.RegistrationNumber, NpgsqlDbType.Text, cancellationToken);
            await WriteAsync(writer, row.Euid, cancellationToken);
            await WriteAsync(writer, row.Name, cancellationToken);
            if (row.Cui is { } cui)
            {
                await writer.WriteAsync(cui, NpgsqlDbType.Bigint, cancellationToken);
            }
            else
            {
                await writer.WriteNullAsync(cancellationToken);
            }

            await writer.WriteAsync(row.CuiState.ToString(), NpgsqlDbType.Text, cancellationToken);
            await WriteAsync(writer, row.LegalForm, cancellationToken);
            if (row.RegistrationDate is { } date)
            {
                await writer.WriteAsync(date, NpgsqlDbType.Date, cancellationToken);
            }
            else
            {
                await writer.WriteNullAsync(cancellationToken);
            }

            foreach (var value in new[]
            {
                row.Country, row.County, row.Locality, row.Street, row.StreetNumber, row.Block, row.Staircase, row.Floor,
                row.Apartment, row.PostalCode, row.Sector, row.AddressDetails, row.Website, row.ParentCountry,
            })
            {
                await WriteAsync(writer, value, cancellationToken);
            }
        }

        await writer.CompleteAsync(cancellationToken);
    }

    private static async Task<(int Pairs, Dictionary<string, int> States)> ResolveGeographyAsync(
        NpgsqlConnection connection, SirutaSeatMatcher matcher, CancellationToken cancellationToken)
    {
        var pairs = new List<(string? County, string? Locality)>();
        await using (var command = new NpgsqlCommand("SELECT DISTINCT county, locality FROM firme_rows", connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                pairs.Add((reader.IsDBNull(0) ? null : reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1)));
            }
        }

        var states = new Dictionary<string, int>();
        await using var writer = await connection.BeginBinaryImportAsync(Sql.CopyGeography, cancellationToken);
        foreach (var (county, locality) in pairs)
        {
            var match = matcher.Match(county, locality);
            states[match.State] = states.GetValueOrDefault(match.State) + 1;
            await writer.StartRowAsync(cancellationToken);
            await WriteAsync(writer, county, cancellationToken);
            await WriteAsync(writer, locality, cancellationToken);
            await writer.WriteAsync(match.State, NpgsqlDbType.Text, cancellationToken);
            await WriteNullableAsync(writer, match.CountyNumber, NpgsqlDbType.Smallint, cancellationToken);
            await WriteNullableAsync(writer, match.UatCode, NpgsqlDbType.Integer, cancellationToken);
            await WriteNullableAsync(writer, match.LocalityCode, NpgsqlDbType.Integer, cancellationToken);
        }

        await writer.CompleteAsync(cancellationToken);
        return (pairs.Count, states);
    }

    internal static async Task WriteAsync(NpgsqlBinaryImporter writer, string? value, CancellationToken cancellationToken)
    {
        if (value is null)
        {
            await writer.WriteNullAsync(cancellationToken);
        }
        else
        {
            await writer.WriteAsync(value, NpgsqlDbType.Text, cancellationToken);
        }
    }

    private static async Task WriteNullableAsync<T>(NpgsqlBinaryImporter writer, T? value, NpgsqlDbType type, CancellationToken cancellationToken)
        where T : struct
    {
        if (value is { } present)
        {
            await writer.WriteAsync(present, type, cancellationToken);
        }
        else
        {
            await writer.WriteNullAsync(cancellationToken);
        }
    }

    internal static async Task<int> ExecuteAsync(
        NpgsqlConnection connection, string sql, CancellationToken cancellationToken, params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 0 };
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<long> ScalarAsync(NpgsqlConnection connection, string sql, int snapshot, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("snapshot", snapshot);
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static class Sql
    {
        public const string CreateStaging = """
            CREATE TEMP TABLE firme_rows (
                row_number integer NOT NULL, registration_number text NOT NULL, euid text, name text, cui bigint,
                cui_state text NOT NULL, legal_form text, registration_date date, country text, county text, locality text,
                street text, street_number text, block text, staircase text, floor text, apartment text, postal_code text,
                sector text, address_details text, website text, parent_country text) ON COMMIT DROP;
            CREATE TEMP TABLE geography_matches (
                county text, locality text, state text NOT NULL, county_number smallint, uat_code integer, locality_code integer
            ) ON COMMIT DROP;
            """;

        public const string CopyStaging = """
            COPY firme_rows (row_number, registration_number, euid, name, cui, cui_state, legal_form, registration_date,
                country, county, locality, street, street_number, block, staircase, floor, apartment, postal_code, sector,
                address_details, website, parent_country) FROM STDIN (FORMAT BINARY)
            """;

        public const string CopyGeography =
            "COPY geography_matches (county, locality, state, county_number, uat_code, locality_code) FROM STDIN (FORMAT BINARY)";

        // One registration per registry number. Several source rows for the same number are merged: a single valid CUI
        // wins over zero or missing ones, other fields take their most frequent value, and every field whose non-blank
        // values disagree is listed in conflicting_fields. Individuals (PFA, II, IF, PF, AF) keep only a coarse address.
        public const string InsertRegistrations = """
            WITH merged AS (
                SELECT registration_number,
                    mode() WITHIN GROUP (ORDER BY euid) AS euid,
                    mode() WITHIN GROUP (ORDER BY name) AS name,
                    CASE WHEN count(DISTINCT cui) = 1 THEN max(cui) END AS cui,
                    CASE WHEN count(DISTINCT cui) = 1 THEN 'Valid'
                         WHEN count(DISTINCT cui) > 1 THEN 'Conflict'
                         ELSE mode() WITHIN GROUP (ORDER BY cui_state) END AS cui_state,
                    mode() WITHIN GROUP (ORDER BY legal_form) AS legal_form,
                    mode() WITHIN GROUP (ORDER BY registration_date) AS registration_date,
                    mode() WITHIN GROUP (ORDER BY country) AS country,
                    mode() WITHIN GROUP (ORDER BY county) AS county,
                    mode() WITHIN GROUP (ORDER BY locality) AS locality,
                    mode() WITHIN GROUP (ORDER BY street) AS street,
                    mode() WITHIN GROUP (ORDER BY street_number) AS street_number,
                    mode() WITHIN GROUP (ORDER BY block) AS block,
                    mode() WITHIN GROUP (ORDER BY staircase) AS staircase,
                    mode() WITHIN GROUP (ORDER BY floor) AS floor,
                    mode() WITHIN GROUP (ORDER BY apartment) AS apartment,
                    mode() WITHIN GROUP (ORDER BY postal_code) AS postal_code,
                    mode() WITHIN GROUP (ORDER BY sector) AS sector,
                    mode() WITHIN GROUP (ORDER BY address_details) AS address_details,
                    mode() WITHIN GROUP (ORDER BY website) AS website,
                    mode() WITHIN GROUP (ORDER BY parent_country) AS parent_country,
                    count(*) AS source_rows,
                    array_remove(ARRAY[
                        CASE WHEN count(DISTINCT euid) > 1 THEN 'euid' END,
                        CASE WHEN count(DISTINCT name) > 1 THEN 'name' END,
                        CASE WHEN count(DISTINCT cui) > 1 THEN 'cui' END,
                        CASE WHEN count(DISTINCT legal_form) > 1 THEN 'legal_form' END,
                        CASE WHEN count(DISTINCT registration_date) > 1 THEN 'registration_date' END,
                        CASE WHEN count(DISTINCT county) > 1 THEN 'county' END,
                        CASE WHEN count(DISTINCT locality) > 1 THEN 'locality' END,
                        CASE WHEN count(DISTINCT street) > 1 THEN 'street' END,
                        CASE WHEN count(DISTINCT street_number) > 1 THEN 'street_number' END,
                        CASE WHEN count(DISTINCT postal_code) > 1 THEN 'postal_code' END,
                        CASE WHEN count(DISTINCT website) > 1 THEN 'website' END
                    ], NULL) AS conflicting_fields
                FROM firme_rows
                GROUP BY registration_number
            ), categorized AS (
                SELECT merged.*,
                    CASE
                        WHEN legal_form IN ('SRL', 'SA', 'SNC', 'SCS', 'SCA', 'SE', 'SCE', 'GIE', 'GEIE', 'RA', 'INCD') THEN 'company'
                        WHEN legal_form IN ('PFA', 'II', 'IF', 'PF', 'AF') THEN 'individual'
                        WHEN legal_form IN ('SC', 'CA', 'OC', 'OC1', 'OC2', 'OC3', 'OC4', 'OC5', 'OC6', 'OC7', 'OCC', 'OCM', 'OCR') THEN 'cooperative'
                        ELSE 'other'
                    END AS form_category
                FROM merged
            )
            INSERT INTO registry.registrations (
                snapshot_id, registration_number, euid, name, cui, cui_state, legal_form, form_category, registration_date,
                country, county, locality, street, street_number, block, staircase, floor, apartment, postal_code, sector,
                address_details, website, parent_country, county_number, uat_code, locality_code, geography_state,
                source_rows, conflicting_fields)
            SELECT @snapshot, c.registration_number, c.euid, c.name, c.cui, c.cui_state, c.legal_form, c.form_category,
                c.registration_date, c.country, c.county, c.locality,
                CASE WHEN c.form_category = 'individual' THEN NULL ELSE c.street END,
                CASE WHEN c.form_category = 'individual' THEN NULL ELSE c.street_number END,
                CASE WHEN c.form_category = 'individual' THEN NULL ELSE c.block END,
                CASE WHEN c.form_category = 'individual' THEN NULL ELSE c.staircase END,
                CASE WHEN c.form_category = 'individual' THEN NULL ELSE c.floor END,
                CASE WHEN c.form_category = 'individual' THEN NULL ELSE c.apartment END,
                c.postal_code, c.sector,
                CASE WHEN c.form_category = 'individual' THEN NULL ELSE c.address_details END,
                c.website, c.parent_country, g.county_number, g.uat_code, g.locality_code, coalesce(g.state, 'LocalityNotGiven'),
                least(c.source_rows, 32767)::smallint, c.conflicting_fields
            FROM categorized c
            LEFT JOIN geography_matches g
                ON coalesce(g.county, '') = coalesce(c.county, '') AND coalesce(g.locality, '') = coalesce(c.locality, '')
            """;

        // A company is every registration sharing a valid CUI (it re-registers when it moves county or is renumbered);
        // without a valid CUI the registration is its own company. The latest registration is the current one.
        public const string InsertCompanies = """
            WITH keyed AS (
                SELECT r.*,
                    CASE WHEN r.cui IS NOT NULL THEN 'C' || r.cui ELSE 'R' || r.registration_number END AS company_key
                FROM registry.registrations r
                WHERE r.snapshot_id = @snapshot
            ), ranked AS (
                SELECT keyed.*,
                    row_number() OVER (PARTITION BY company_key ORDER BY registration_date DESC NULLS LAST, registration_number DESC) AS position,
                    count(*) OVER (PARTITION BY company_key) AS registration_count,
                    min(registration_date) OVER (PARTITION BY company_key) AS first_registration_date
                FROM keyed
            )
            INSERT INTO registry.companies (
                snapshot_id, company_key, cui, registration_number, name, legal_form, form_category, registration_date,
                first_registration_date, registration_count, county_number, uat_code, locality_code, geography_state, website)
            SELECT @snapshot, company_key, cui, registration_number, name, legal_form, form_category, registration_date,
                first_registration_date, least(registration_count, 32767)::smallint, county_number, uat_code, locality_code,
                geography_state, website
            FROM ranked
            WHERE position = 1
            """;
    }
}
