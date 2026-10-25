using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using BusinessIntelligence.Modules.Platform.Persistence;
using BusinessIntelligence.Modules.Registry.Sources;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace BusinessIntelligence.Modules.Registry.Onrc;

/// <summary>
/// Imports the files that annex an OD_FIRME snapshot (statuses and authorized activities). They must have the same
/// reference date as the published snapshot and are applied to it in one transaction; a new OD_FIRME snapshot deletes them.
/// </summary>
public sealed class ImportOnrcAnnexes(AppDbContext database, TimeProvider time)
{
    public const string StatusKind = "onrc-firme-status";
    public const string ActivitiesKind = "onrc-firme-activities";
    public const string ParserVersion = "onrc-annex-v1";

    private static readonly string[] StatusHeader = ["COD_INMATRICULARE", "COD"];
    private static readonly string[] ActivityHeader = ["COD_INMATRICULARE", "COD_CAEN_AUTORIZAT", "VER_CAEN_AUTORIZAT"];

    public Task<ImportResult> StatusesAsync(string path, CatalogFile file, CancellationToken cancellationToken) =>
        ImportAsync(path, file, StatusKind, StatusHeader, Sql.CreateStatusStaging, Sql.CopyStatuses, WriteStatusAsync, ApplyStatusesAsync, cancellationToken);

    public Task<ImportResult> ActivitiesAsync(string path, CatalogFile file, CancellationToken cancellationToken) =>
        ImportAsync(path, file, ActivitiesKind, ActivityHeader, Sql.CreateActivityStaging, Sql.CopyActivities, WriteActivityAsync, ApplyActivitiesAsync, cancellationToken);

    private async Task<ImportResult> ImportAsync(
        string path, CatalogFile file, string kind, string[] header, string createStaging, string copy,
        Func<NpgsqlBinaryImporter, string[], AnnexCounts, CancellationToken, Task> writeRow,
        Func<NpgsqlConnection, int, Dictionary<string, object>, CancellationToken, Task> apply,
        CancellationToken cancellationToken)
    {
        if (file.Kind != kind)
        {
            throw new ArgumentException($"Catalog entry {file.Id} is not a {kind} file.", nameof(file));
        }

        var existing = await database.SourceFiles.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Sha256 == file.Sha256 && item.ParserVersion == ParserVersion, cancellationToken);
        if (existing is not null)
        {
            return new ImportResult(ImportOutcome.AlreadyImported, existing.Id, new Dictionary<string, object>());
        }

        var snapshot = await database.PublishedDatasets.AsNoTracking()
            .Where(item => item.Kind == ImportOnrcFirme.Kind)
            .Join(database.SourceFiles, item => item.SourceFileId, source => source.Id, (_, source) => new { source.Id, source.ReferenceDate })
            .FirstOrDefaultAsync(cancellationToken);
        if (snapshot is null || snapshot.ReferenceDate != file.ReferenceDate)
        {
            throw new InvalidOperationException(
                $"{file.FileName} belongs to the {file.ReferenceDate:yyyy-MM-dd} registry snapshot; publish that OD_FIRME snapshot first.");
        }

        database.Database.SetCommandTimeout(TimeSpan.FromHours(1));
        await database.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)database.Database.GetDbConnection();
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await ImportOnrcFirme.ExecuteAsync(connection, createStaging, cancellationToken);
        var counts = new AnnexCounts();
        await using (var stream = VerifiedFileStream.Open(path, file))
        {
            await using (var writer = await connection.BeginBinaryImportAsync(copy, cancellationToken))
            {
                await foreach (var fields in ReadAsync(stream, header, counts, cancellationToken))
                {
                    await writeRow(writer, fields, counts, cancellationToken);
                }

                await writer.CompleteAsync(cancellationToken);
            }

            stream.EnsureComplete();
        }

        var report = new Dictionary<string, object>
        {
            ["rows"] = counts.Rows,
            ["rowsWithoutRegistrationNumber"] = counts.WithoutRegistrationNumber,
            ["rowsWithInvalidValues"] = counts.Invalid,
        };
        await apply(connection, snapshot.Id, report, cancellationToken);
        var source = RegistryImports.NewSourceFile(file, ParserVersion, time, JsonSerializer.Serialize(report));
        database.SourceFiles.Add(source);
        await database.SaveChangesAsync(cancellationToken);
        await RegistryImports.PublishAsync(database, kind, source.Id, time, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await RegistryImports.VacuumAsync(database,
            "registry.registrations, registry.companies, registry.registration_statuses, registry.company_activities", cancellationToken);
        return new ImportResult(ImportOutcome.Published, source.Id, report);
    }

    private static async Task WriteStatusAsync(NpgsqlBinaryImporter writer, string[] fields, AnnexCounts counts, CancellationToken cancellationToken)
    {
        if (!int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var code))
        {
            counts.Invalid++;
            return;
        }

        await writer.StartRowAsync(cancellationToken);
        await writer.WriteAsync(fields[0], NpgsqlDbType.Text, cancellationToken);
        await writer.WriteAsync(code, NpgsqlDbType.Integer, cancellationToken);
    }

    private static async Task WriteActivityAsync(NpgsqlBinaryImporter writer, string[] fields, AnnexCounts counts, CancellationToken cancellationToken)
    {
        // Codes are kept as published, including the 2–3 digit group codes of the old versions.
        if (fields[1].Length is < 2 or > 4 || !fields[1].All(char.IsAsciiDigit)
            || !short.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out var version))
        {
            counts.Invalid++;
            return;
        }

        await writer.StartRowAsync(cancellationToken);
        await writer.WriteAsync(fields[0], NpgsqlDbType.Text, cancellationToken);
        await writer.WriteAsync(fields[1], NpgsqlDbType.Text, cancellationToken);
        await writer.WriteAsync(version, NpgsqlDbType.Smallint, cancellationToken);
    }

    private static async Task ApplyStatusesAsync(NpgsqlConnection connection, int snapshot, Dictionary<string, object> report, CancellationToken cancellationToken)
    {
        await ImportOnrcFirme.ExecuteAsync(connection, "DELETE FROM registry.registration_statuses WHERE snapshot_id = @snapshot", cancellationToken, ("snapshot", snapshot));
        report["statuses"] = await ImportOnrcFirme.ExecuteAsync(connection, Sql.InsertStatuses, cancellationToken, ("snapshot", snapshot));
        report["rowsForUnknownRegistrations"] = await ScalarAsync(connection, Sql.UnknownStatusRegistrations, snapshot, cancellationToken);
        report["unknownStatusCodes"] = await ScalarAsync(connection,
            "SELECT count(DISTINCT code) FROM status_rows s WHERE NOT EXISTS (SELECT 1 FROM registry.status_codes c WHERE c.code = s.code)", snapshot, cancellationToken);
        await ImportOnrcFirme.ExecuteAsync(connection,
            "UPDATE registry.registrations SET lifecycle = 'Unknown' WHERE snapshot_id = @snapshot AND lifecycle <> 'Unknown'", cancellationToken, ("snapshot", snapshot));
        await ImportOnrcFirme.ExecuteAsync(connection, Sql.UpdateRegistrationLifecycle, cancellationToken, ("snapshot", snapshot));
        await ImportOnrcFirme.ExecuteAsync(connection, Sql.UpdateCompanyLifecycle, cancellationToken, ("snapshot", snapshot));
        report["companiesByLifecycle"] = await GroupCountsAsync(connection,
            "SELECT lifecycle, count(*) FROM registry.companies WHERE snapshot_id = @snapshot GROUP BY lifecycle", snapshot, cancellationToken);
    }

    private static async Task ApplyActivitiesAsync(NpgsqlConnection connection, int snapshot, Dictionary<string, object> report, CancellationToken cancellationToken)
    {
        await ImportOnrcFirme.ExecuteAsync(connection, "DELETE FROM registry.company_activities WHERE snapshot_id = @snapshot", cancellationToken, ("snapshot", snapshot));
        report["companyActivities"] = await ImportOnrcFirme.ExecuteAsync(connection, Sql.InsertActivities, cancellationToken, ("snapshot", snapshot));
        await ImportOnrcFirme.ExecuteAsync(connection,
            "UPDATE registry.companies SET caen_version = NULL WHERE snapshot_id = @snapshot AND caen_version IS NOT NULL", cancellationToken, ("snapshot", snapshot));
        await ImportOnrcFirme.ExecuteAsync(connection, Sql.UpdateCompanyCaenVersion, cancellationToken, ("snapshot", snapshot));
        report["rowsForUnknownRegistrations"] = await ScalarAsync(connection, Sql.UnknownActivityRegistrations, snapshot, cancellationToken);
        report["companiesByCaenVersion"] = await GroupCountsAsync(connection,
            "SELECT coalesce(caen_version::text, 'none'), count(*) FROM registry.companies WHERE snapshot_id = @snapshot GROUP BY 1", snapshot, cancellationToken);
        report["activityCodesNotInNomenclature"] = await ScalarAsync(connection, Sql.UnknownActivityCodes, snapshot, cancellationToken);
    }

    private static async IAsyncEnumerable<string[]> ReadAsync(
        Stream stream, string[] header, AnnexCounts counts, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), true, 1 << 16, leaveOpen: true);
        if ((await reader.ReadLineAsync(cancellationToken))?.Split('^').SequenceEqual(header) != true)
        {
            throw new InvalidDataException("Unexpected header; expected " + string.Join('^', header));
        }

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (line.Length == 0)
            {
                continue;
            }

            var fields = line.Split('^');
            if (fields.Length != header.Length)
            {
                throw new InvalidDataException($"Expected {header.Length} fields: {line}");
            }

            counts.Rows++;
            for (var index = 0; index < fields.Length; index++)
            {
                fields[index] = fields[index].Trim();
            }

            if (fields[0].Length == 0)
            {
                counts.WithoutRegistrationNumber++;
                continue;
            }

            yield return fields;
        }
    }

    private static async Task<long> ScalarAsync(NpgsqlConnection connection, string sql, int snapshot, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 0 };
        command.Parameters.AddWithValue("snapshot", snapshot);
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task<Dictionary<string, long>> GroupCountsAsync(NpgsqlConnection connection, string sql, int snapshot, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 0 };
        command.Parameters.AddWithValue("snapshot", snapshot);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new Dictionary<string, long>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result[reader.GetString(0)] = reader.GetInt64(1);
        }

        return result;
    }

    private sealed class AnnexCounts
    {
        public int Rows { get; set; }
        public int WithoutRegistrationNumber { get; set; }
        public int Invalid { get; set; }
    }

    private static class Sql
    {
        public const string CreateStatusStaging =
            "CREATE TEMP TABLE status_rows (registration_number text NOT NULL, code integer NOT NULL) ON COMMIT DROP";

        public const string CopyStatuses = "COPY status_rows (registration_number, code) FROM STDIN (FORMAT BINARY)";

        public const string CreateActivityStaging =
            "CREATE TEMP TABLE activity_rows (registration_number text NOT NULL, code text NOT NULL, version smallint NOT NULL) ON COMMIT DROP";

        public const string CopyActivities = "COPY activity_rows (registration_number, code, version) FROM STDIN (FORMAT BINARY)";

        public const string InsertStatuses = """
            INSERT INTO registry.registration_statuses (snapshot_id, registration_number, status_code)
            SELECT DISTINCT @snapshot, s.registration_number, s.code
            FROM status_rows s
            JOIN registry.registrations r ON r.snapshot_id = @snapshot AND r.registration_number = s.registration_number
            """;

        public const string UnknownStatusRegistrations = """
            SELECT count(*) FROM status_rows s
            WHERE NOT EXISTS (SELECT 1 FROM registry.registrations r WHERE r.snapshot_id = @snapshot AND r.registration_number = s.registration_number)
            """;

        public static readonly string UpdateRegistrationLifecycle = $"""
            UPDATE registry.registrations r SET lifecycle = l.lifecycle
            FROM (
                SELECT s.registration_number, {Lifecycle.SqlCase()} AS lifecycle
                FROM registry.registration_statuses s
                WHERE s.snapshot_id = @snapshot
                GROUP BY s.registration_number
            ) l
            WHERE r.snapshot_id = @snapshot AND r.registration_number = l.registration_number
            """;

        public const string UpdateCompanyLifecycle = """
            UPDATE registry.companies c SET lifecycle = r.lifecycle
            FROM registry.registrations r
            WHERE c.snapshot_id = @snapshot AND r.snapshot_id = @snapshot AND r.registration_number = c.registration_number
                AND c.lifecycle IS DISTINCT FROM r.lifecycle
            """;

        // Only the company's current registration, in its newest CAEN version: older registrations of a company that moved
        // county and superseded CAEN versions describe the past, not what the company is authorized to do now.
        public const string InsertActivities = """
            WITH current_rows AS (
                SELECT c.company_key, a.code, a.version,
                    max(a.version) OVER (PARTITION BY c.company_key) AS newest
                FROM activity_rows a
                JOIN registry.companies c ON c.snapshot_id = @snapshot AND c.registration_number = a.registration_number
            )
            INSERT INTO registry.company_activities (snapshot_id, company_key, caen_version, caen_code)
            SELECT DISTINCT @snapshot, company_key, version, code
            FROM current_rows
            WHERE version = newest
            """;

        public const string UpdateCompanyCaenVersion = """
            UPDATE registry.companies c SET caen_version = a.version
            FROM (SELECT company_key, max(caen_version) AS version FROM registry.company_activities WHERE snapshot_id = @snapshot GROUP BY company_key) a
            WHERE c.snapshot_id = @snapshot AND c.company_key = a.company_key
            """;

        public const string UnknownActivityRegistrations = """
            SELECT count(*) FROM activity_rows a
            WHERE NOT EXISTS (SELECT 1 FROM registry.registrations r WHERE r.snapshot_id = @snapshot AND r.registration_number = a.registration_number)
            """;

        public const string UnknownActivityCodes = """
            SELECT count(*) FROM registry.company_activities a
            WHERE a.snapshot_id = @snapshot
                AND NOT EXISTS (SELECT 1 FROM registry.caen n WHERE n.version = a.caen_version AND n.code = a.caen_code)
            """;
    }
}
