using System.Text.Json;
using BusinessIntelligence.Modules.Platform.Persistence;
using BusinessIntelligence.Modules.Registry.Geography;
using BusinessIntelligence.Modules.Registry.Onrc;
using BusinessIntelligence.Modules.Registry.Sources;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace BusinessIntelligence.Modules.Registry.History;

/// <summary>
/// Applies registry snapshots to <c>registry.registration_periods</c> in date order, one transaction per snapshot.
/// A registration that looks the same as in the previous snapshot extends its period; any change in name, CUI, place
/// or lifecycle opens a new one. Differences that only come from the publisher changing its format are not changes:
/// names are compared after folding diacritics, punctuation and legal-form spellings; places by SIRUTA code; CUIs and
/// places only when both sides have a value.
/// </summary>
public sealed class ImportRegistryHistory(AppDbContext database, TimeProvider time)
{
    public const string ParserVersion = LegacySnapshotReader.ParserVersion;
    private const string HistoryKindPrefix = "onrc-history-";

    /// <summary>Snapshot dates that have pinned files, oldest first.</summary>
    public static IReadOnlyList<DateOnly> AvailableSnapshots() =>
        [.. Files().Select(file => file.ReferenceDate).Distinct().Order()];

    public Task<ImportResult> ExecuteAsync(DateOnly snapshot, string rawRoot, CancellationToken cancellationToken) =>
        ExecuteAsync(snapshot, [.. Files().Where(file => file.ReferenceDate == snapshot)], rawRoot, cancellationToken);

    /// <summary>Applies the given files of one snapshot; tests pass their own pinned fixtures.</summary>
    public async Task<ImportResult> ExecuteAsync(DateOnly snapshot, List<CatalogFile> files, string rawRoot, CancellationToken cancellationToken)
    {
        if (files.Count == 0)
        {
            throw new ArgumentException($"No pinned registry files for {snapshot:yyyy-MM-dd}.");
        }

        var latest = await database.HistorySnapshots.AsNoTracking().MaxAsync(item => (DateOnly?)item.SnapshotDate, cancellationToken);
        if (latest == snapshot)
        {
            return new ImportResult(ImportOutcome.AlreadyImported, 0, new Dictionary<string, object>());
        }

        if (latest > snapshot)
        {
            throw new InvalidOperationException(
                $"History already reaches {latest:yyyy-MM-dd}; snapshots apply in date order. Rebuild the history to add an older one.");
        }

        var places = await database.Siruta.AsNoTracking().ToListAsync(cancellationToken);
        if (places.Count == 0)
        {
            throw new InvalidOperationException("Import SIRUTA before the registry history.");
        }

        database.Database.SetCommandTimeout(TimeSpan.FromHours(1));
        await database.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)database.Database.GetDbConnection();
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await Execute(connection, Sql.CreateStaging, cancellationToken);

        var counts = new HistoryReadCounts();
        if (files.Any(file => file.Kind is "onrc-firme" or "onrc-history-firme"))
        {
            await CopyCurrentLayoutAsync(connection, files, rawRoot, counts, cancellationToken);
        }
        else
        {
            await CopyLegacyLayoutAsync(connection, files, rawRoot, counts, cancellationToken);
        }

        var geography = await ResolveGeographyAsync(connection, new SirutaSeatMatcher(places), cancellationToken);
        await Execute(connection, Sql.Merge, cancellationToken);
        var extended = latest is { } previous
            ? await Execute(connection, Sql.Extend, cancellationToken, ("date", snapshot), ("previous", previous))
            : 0;
        var opened = await Execute(connection, Sql.Open, cancellationToken, ("date", snapshot));
        var report = new Dictionary<string, object>
        {
            ["files"] = files.Select(file => file.Id).ToArray(),
            ["rows"] = counts.Rows,
            ["rowsWithoutRegistrationNumber"] = counts.WithoutRegistrationNumber,
            ["joinedLines"] = counts.JoinedLines,
            ["mergedExtraFields"] = counts.MergedExtraFields,
            ["registrations"] = await Scalar(connection, "SELECT count(*) FROM history_merged", cancellationToken),
            ["periodsExtended"] = extended,
            ["periodsOpened"] = opened,
            ["geographyStatesByPair"] = geography,
        };
        database.HistorySnapshots.Add(new HistorySnapshot
        {
            SnapshotDate = snapshot,
            ImportedAt = time.GetUtcNow(),
            ParserVersion = ParserVersion,
            Report = JsonSerializer.Serialize(report),
        });
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ImportResult(ImportOutcome.Published, 0, report);
    }

    /// <summary>Deletes the whole history so snapshots can be applied again from the oldest.</summary>
    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        // TRUNCATE rather than DELETE: it gives the disk space back instead of leaving gigabytes of dead rows.
        await database.Database.ExecuteSqlRawAsync(
            "TRUNCATE registry.registration_periods, registry.history_snapshots", cancellationToken);
    }

    private static IEnumerable<CatalogFile> Files() =>
        SourceCatalog.All.Where(file => file.Kind.StartsWith(HistoryKindPrefix, StringComparison.Ordinal)
            || file.Kind is ImportOnrcFirme.Kind or ImportOnrcAnnexes.StatusKind);

    private static async Task CopyLegacyLayoutAsync(
        NpgsqlConnection connection, List<CatalogFile> files, string rawRoot, HistoryReadCounts counts, CancellationToken cancellationToken)
    {
        await using var writer = await connection.BeginBinaryImportAsync(Sql.CopyRows, cancellationToken);
        foreach (var kind in new[] { "active-office", "active-no-office", "struck-office", "struck-no-office" })
        {
            var parts = files.Where(file => file.Kind == HistoryKindPrefix + kind).OrderBy(file => file.Id, StringComparer.Ordinal).ToList();
            var streams = parts.Select(file => VerifiedFileStream.Open(Path.Combine(rawRoot, file.LocalPath!), file)).ToList();
            try
            {
                await foreach (var row in LegacySnapshotReader.ReadAsync(streams, kind.StartsWith("struck", StringComparison.Ordinal),
                    !kind.EndsWith("no-office", StringComparison.Ordinal), counts, cancellationToken))
                {
                    var lifecycle = row.StatusCodes.Length > 0 ? Lifecycle.FromCodes(row.StatusCodes)
                        : row.StruckOffFile ? Lifecycle.StruckOff : Lifecycle.Unknown;
                    await WriteRowAsync(writer, row, lifecycle, cancellationToken);
                }

                foreach (var stream in streams)
                {
                    stream.EnsureComplete();
                }
            }
            finally
            {
                foreach (var stream in streams)
                {
                    await stream.DisposeAsync();
                }
            }
        }

        await writer.CompleteAsync(cancellationToken);
    }

    private static async Task CopyCurrentLayoutAsync(
        NpgsqlConnection connection, List<CatalogFile> files, string rawRoot, HistoryReadCounts counts, CancellationToken cancellationToken)
    {
        var firme = files.Single(file => file.Kind is "onrc-firme" or "onrc-history-firme");
        var firmeCounts = new FirmeReadCounts();
        await using (var writer = await connection.BeginBinaryImportAsync(Sql.CopyRows, cancellationToken))
        await using (var stream = VerifiedFileStream.Open(Path.Combine(rawRoot, firme.LocalPath!), firme))
        {
            await foreach (var row in OnrcFirmeReader.ReadAsync(stream, firmeCounts, cancellationToken))
            {
                var street = string.Join(", ", new[] { row.Street, row.StreetNumber is null ? null : "Nr. " + row.StreetNumber }.Where(part => part is not null));
                var address = string.Join(", ", new[] { row.Locality, street.Length == 0 ? null : street, row.County }.Where(part => part is not null));
                await WriteRowAsync(writer, new HistoryRow(row.RegistrationNumber, row.Name, row.Cui, row.CuiState, row.County, row.Locality,
                    address.Length == 0 ? null : address, [], false), Lifecycle.Unknown, cancellationToken);
            }

            await writer.CompleteAsync(cancellationToken);
            stream.EnsureComplete();
        }

        counts.Rows = firmeCounts.Rows;
        counts.WithoutRegistrationNumber = firmeCounts.WithoutRegistrationNumber;
        var status = files.Single(file => file.Kind is "onrc-firme-status" or "onrc-history-status");
        await using (var writer = await connection.BeginBinaryImportAsync("COPY history_status (registration_number, status_code) FROM STDIN (FORMAT BINARY)", cancellationToken))
        await using (var stream = VerifiedFileStream.Open(Path.Combine(rawRoot, status.LocalPath!), status))
        {
            using var reader = new StreamReader(stream, leaveOpen: true);
            await reader.ReadLineAsync(cancellationToken);
            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                var cells = line.Split('^');
                if (cells.Length == 2 && cells[0].Trim().Length > 0 && int.TryParse(cells[1], out var code))
                {
                    await writer.StartRowAsync(cancellationToken);
                    await writer.WriteAsync(cells[0].Trim(), NpgsqlDbType.Text, cancellationToken);
                    await writer.WriteAsync(code, NpgsqlDbType.Integer, cancellationToken);
                }
            }

            await writer.CompleteAsync(cancellationToken);
            stream.EnsureComplete();
        }

        await Execute(connection, Sql.ApplyStatuses, cancellationToken);
    }

    private static async Task WriteRowAsync(NpgsqlBinaryImporter writer, HistoryRow row, string lifecycle, CancellationToken cancellationToken)
    {
        // Sole traders (registry numbers starting with F) keep only a coarse place, as in the current registry tables.
        var individual = row.RegistrationNumber.StartsWith('F');
        await writer.StartRowAsync(cancellationToken);
        await writer.WriteAsync(row.RegistrationNumber, NpgsqlDbType.Text, cancellationToken);
        await ImportOnrcFirme.WriteAsync(writer, row.Name, cancellationToken);
        await ImportOnrcFirme.WriteAsync(writer, NameKey(row.Name), cancellationToken);
        if (row.Cui is { } cui)
        {
            await writer.WriteAsync(cui, NpgsqlDbType.Bigint, cancellationToken);
        }
        else
        {
            await writer.WriteNullAsync(cancellationToken);
        }

        await writer.WriteAsync(row.CuiState.ToString(), NpgsqlDbType.Text, cancellationToken);
        await ImportOnrcFirme.WriteAsync(writer, row.County, cancellationToken);
        await ImportOnrcFirme.WriteAsync(writer, row.Locality, cancellationToken);
        await ImportOnrcFirme.WriteAsync(writer, individual ? null : row.Address, cancellationToken);
        await writer.WriteAsync(row.StatusCodes, NpgsqlDbType.Array | NpgsqlDbType.Integer, cancellationToken);
        await writer.WriteAsync(row.StruckOffFile, NpgsqlDbType.Boolean, cancellationToken);
        await writer.WriteAsync(lifecycle, NpgsqlDbType.Text, cancellationToken);
    }

    /// <summary>Name compared across snapshots: folded, with legal-form spellings unified (PERSOANĂ FIZICĂ AUTORIZATĂ = PFA, S.R.L. = SRL).</summary>
    public static string? NameKey(string? name)
    {
        if (name is null)
        {
            return null;
        }

        var folded = " " + SirutaSeatMatcher.Fold(name) + " ";
        foreach (var (from, to) in NameForms)
        {
            folded = folded.Replace(from, to, StringComparison.Ordinal);
        }

        return folded.Trim();
    }

    private static readonly (string From, string To)[] NameForms =
    [
        (" PERSOANA FIZICA AUTORIZATA ", " PFA "),
        (" INTREPRINDERE INDIVIDUALA ", " II "),
        (" INTREPRINDERE FAMILIALA ", " IF "),
        (" S R L D ", " SRL D "),
        (" S R L ", " SRL "),
        (" S A ", " SA "),
        (" S N C ", " SNC "),
        (" S C S ", " SCS "),
    ];

    private static async Task<Dictionary<string, int>> ResolveGeographyAsync(NpgsqlConnection connection, SirutaSeatMatcher matcher, CancellationToken cancellationToken)
    {
        var pairs = new List<(string? County, string? Locality)>();
        await using (var command = new NpgsqlCommand("SELECT DISTINCT county, locality FROM history_rows", connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                pairs.Add((reader.IsDBNull(0) ? null : reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1)));
            }
        }

        var states = new Dictionary<string, int>();
        await using var writer = await connection.BeginBinaryImportAsync(
            "COPY history_geography (county, locality, county_number, uat_code, locality_code) FROM STDIN (FORMAT BINARY)", cancellationToken);
        foreach (var (county, locality) in pairs)
        {
            var match = matcher.Match(county, locality);
            states[match.State] = states.GetValueOrDefault(match.State) + 1;
            await writer.StartRowAsync(cancellationToken);
            await ImportOnrcFirme.WriteAsync(writer, county, cancellationToken);
            await ImportOnrcFirme.WriteAsync(writer, locality, cancellationToken);
            await WriteNullableAsync(writer, match.CountyNumber, NpgsqlDbType.Smallint, cancellationToken);
            await WriteNullableAsync(writer, match.UatCode, NpgsqlDbType.Integer, cancellationToken);
            await WriteNullableAsync(writer, match.LocalityCode, NpgsqlDbType.Integer, cancellationToken);
        }

        await writer.CompleteAsync(cancellationToken);
        return states;
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

    private static async Task<int> Execute(NpgsqlConnection connection, string sql, CancellationToken cancellationToken, params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 0 };
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<long> Scalar(NpgsqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 0 };
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static class Sql
    {
        public const string CreateStaging = """
            CREATE TEMP TABLE history_rows (
                registration_number text NOT NULL, name text, name_key text, cui bigint, cui_state text NOT NULL, county text,
                locality text, address text, status_codes integer[] NOT NULL, struck_off_file boolean NOT NULL, lifecycle text NOT NULL
            ) ON COMMIT DROP;
            CREATE TEMP TABLE history_status (registration_number text NOT NULL, status_code integer NOT NULL) ON COMMIT DROP;
            CREATE TEMP TABLE history_geography (county text, locality text, county_number smallint, uat_code integer, locality_code integer) ON COMMIT DROP;
            """;

        public const string CopyRows = """
            COPY history_rows (registration_number, name, name_key, cui, cui_state, county, locality, address, status_codes,
                struck_off_file, lifecycle) FROM STDIN (FORMAT BINARY)
            """;

        public static readonly string ApplyStatuses = $"""
            UPDATE history_rows h SET status_codes = s.codes, lifecycle = s.lifecycle
            FROM (
                SELECT s.registration_number, array_agg(DISTINCT s.status_code ORDER BY s.status_code) AS codes, {Lifecycle.SqlCase()} AS lifecycle
                FROM history_status s
                GROUP BY s.registration_number
            ) s
            WHERE h.registration_number = s.registration_number
            """;

        // One row per registry number: a row from the active files wins over one from the struck-off files, then the
        // most complete row. Geography joins on the exact county and locality text.
        public const string Merge = """
            CREATE TEMP TABLE history_merged ON COMMIT DROP AS
            SELECT DISTINCT ON (h.registration_number) h.*, g.county_number, g.uat_code, g.locality_code
            FROM history_rows h
            LEFT JOIN history_geography g ON coalesce(g.county, '') = coalesce(h.county, '') AND coalesce(g.locality, '') = coalesce(h.locality, '')
            ORDER BY h.registration_number, h.struck_off_file, num_nonnulls(h.name, h.cui, h.county, h.locality, h.address) DESC;
            ALTER TABLE history_merged ADD COLUMN extended boolean NOT NULL DEFAULT false;
            CREATE INDEX ON history_merged (registration_number);
            ANALYZE history_merged;
            """;

        private const string Same = """
            p.name_key IS NOT DISTINCT FROM m.name_key
            AND p.lifecycle = m.lifecycle
            AND (p.cui IS NULL OR m.cui IS NULL OR p.cui = m.cui)
            AND (CASE WHEN p.uat_code IS NOT NULL AND m.uat_code IS NOT NULL THEN p.uat_code = m.uat_code
                      WHEN p.county_number IS NOT NULL AND m.county_number IS NOT NULL THEN p.county_number = m.county_number
                      ELSE true END)
            """;

        // Extended registrations are flagged in history_merged so Open needs no anti-join on registration_periods, whose
        // statistics cannot see the rows just updated in this transaction (the planner then picked a plan that ran for hours).
        public static readonly string Extend = $"""
            WITH extended AS (
            UPDATE registry.registration_periods p
            SET valid_to = @date, status_codes = m.status_codes, address = coalesce(m.address, p.address),
                cui = coalesce(p.cui, m.cui), uat_code = coalesce(p.uat_code, m.uat_code), locality_code = coalesce(p.locality_code, m.locality_code)
            FROM history_merged m
            WHERE p.registration_number = m.registration_number AND p.valid_to = @previous AND {Same}
            RETURNING p.registration_number)
            UPDATE history_merged m SET extended = true FROM extended e WHERE m.registration_number = e.registration_number
            """;

        public const string Open = """
            INSERT INTO registry.registration_periods (
                registration_number, valid_from, valid_to, name, name_key, cui, cui_state, county, locality, address, county_number,
                uat_code, locality_code, lifecycle, status_codes)
            SELECT m.registration_number, @date, @date, m.name, m.name_key, m.cui, m.cui_state, m.county, m.locality, m.address,
                m.county_number, m.uat_code, m.locality_code, m.lifecycle, m.status_codes
            FROM history_merged m
            WHERE NOT m.extended
            """;
    }
}
