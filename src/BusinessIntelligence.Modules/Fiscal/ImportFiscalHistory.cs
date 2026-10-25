using System.IO.Compression;
using System.Text.Json;
using BusinessIntelligence.Modules.Platform.Persistence;
using BusinessIntelligence.Modules.Registry;
using BusinessIntelligence.Modules.Registry.Geography;
using BusinessIntelligence.Modules.Registry.History;
using BusinessIntelligence.Modules.Registry.Sources;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace BusinessIntelligence.Modules.Fiscal;

/// <summary>
/// Applies MF identification releases to <c>fiscal.taxpayer_periods</c> in date order, one transaction per release.
/// Individuals keep only name, county, locality, sector and postal code: their street, phone and fax are dropped in
/// staging and never stored. Releases before 2022 do not give the taxpayer type, so it comes from
/// <c>fiscal.taxpayer_kinds</c> (built from the newer releases), then from the registry reference letter; a taxpayer
/// with neither is treated as an individual.
/// </summary>
public sealed class ImportFiscalHistory(AppDbContext database, TimeProvider time)
{
    public const string Kind = "mf-identification";
    public const string ParserVersion = MfIdentificationReader.ParserVersion;

    /// <summary>The first release that states the taxpayer type.</summary>
    public static readonly DateOnly TypedReleases = new(2022, 1, 1);

    /// <summary>Release dates that have pinned files, oldest first.</summary>
    public static IReadOnlyList<DateOnly> AvailableReleases() =>
        [.. SourceCatalog.OfKind(Kind).Select(file => file.ReferenceDate).Distinct().Order()];

    public static List<CatalogFile> Files(DateOnly release) =>
        [.. SourceCatalog.OfKind(Kind).Where(file => file.ReferenceDate == release).OrderBy(file => file.Id, StringComparer.Ordinal)];

    /// <summary>
    /// Replaces <c>fiscal.taxpayer_kinds</c> with the type each CUI has in the given (2022 or later) files; the newest
    /// release wins. Returns the number of CUIs.
    /// </summary>
    public async Task<long> BuildKindsAsync(IEnumerable<CatalogFile> files, string rawRoot, CancellationToken cancellationToken)
    {
        database.Database.SetCommandTimeout(TimeSpan.FromHours(1));
        await database.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)database.Database.GetDbConnection();
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await Execute(connection, "CREATE TEMP TABLE fiscal_kinds (cui bigint NOT NULL, kind text NOT NULL, release date NOT NULL) ON COMMIT DROP", cancellationToken);
        await using (var writer = await connection.BeginBinaryImportAsync("COPY fiscal_kinds (cui, kind, release) FROM STDIN (FORMAT BINARY)", cancellationToken))
        {
            foreach (var file in files.Where(file => file.ReferenceDate >= TypedReleases))
            {
                await foreach (var row in ReadFileAsync(file, rawRoot, new IdentificationReadCounts(), cancellationToken))
                {
                    if (row.Kind is { } kind)
                    {
                        await writer.StartRowAsync(cancellationToken);
                        await writer.WriteAsync(row.Cui, NpgsqlDbType.Bigint, cancellationToken);
                        await writer.WriteAsync(kind, NpgsqlDbType.Text, cancellationToken);
                        await writer.WriteAsync(file.ReferenceDate, NpgsqlDbType.Date, cancellationToken);
                    }
                }
            }

            await writer.CompleteAsync(cancellationToken);
        }

        await Execute(connection, """
            TRUNCATE fiscal.taxpayer_kinds;
            INSERT INTO fiscal.taxpayer_kinds (cui, kind, release_date)
            SELECT DISTINCT ON (cui) cui, kind, release FROM fiscal_kinds ORDER BY cui, release DESC
            """, cancellationToken);
        var count = await Scalar(connection, "SELECT count(*) FROM fiscal.taxpayer_kinds", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return count;
    }

    public Task<ImportResult> ExecuteAsync(DateOnly release, string rawRoot, CancellationToken cancellationToken) =>
        ExecuteAsync(release, Files(release), rawRoot, cancellationToken);

    /// <summary>Applies the given files of one release; tests pass their own pinned fixtures.</summary>
    public async Task<ImportResult> ExecuteAsync(DateOnly release, List<CatalogFile> files, string rawRoot, CancellationToken cancellationToken)
    {
        if (files.Count == 0)
        {
            throw new ArgumentException($"No pinned MF identification files for {release:yyyy-MM-dd}.");
        }

        var latest = await database.FiscalReleases.AsNoTracking().MaxAsync(item => (DateOnly?)item.ReleaseDate, cancellationToken);
        if (latest == release)
        {
            return new ImportResult(ImportOutcome.AlreadyImported, 0, new Dictionary<string, object>());
        }

        if (latest > release)
        {
            throw new InvalidOperationException(
                $"Fiscal history already reaches {latest:yyyy-MM-dd}; releases apply in date order. Rebuild it to add an older one.");
        }

        database.Database.SetCommandTimeout(TimeSpan.FromHours(1));
        await database.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)database.Database.GetDbConnection();
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await Execute(connection, Sql.CreateStaging, cancellationToken);

        var counts = new IdentificationReadCounts();
        await using (var writer = await connection.BeginBinaryImportAsync(Sql.Copy, cancellationToken))
        {
            foreach (var file in files)
            {
                await foreach (var row in ReadFileAsync(file, rawRoot, counts, cancellationToken))
                {
                    await WriteRowAsync(writer, row, cancellationToken);
                }
            }

            await writer.CompleteAsync(cancellationToken);
        }

        await Execute(connection, Sql.Merge, cancellationToken);
        var stated = await Scalar(connection, "SELECT count(*) FROM fiscal_merged WHERE kind IS NOT NULL", cancellationToken);
        await Execute(connection, Sql.InferKinds, cancellationToken);
        var kinds = await Scalar(connection, "SELECT count(*) FROM fiscal_merged WHERE kind IS NOT NULL", cancellationToken);
        var withoutContacts = await Execute(connection, Sql.DropPersonalFields, cancellationToken);
        var extended = await Execute(connection, Sql.Extend, cancellationToken, ("release", release));
        await Execute(connection, Sql.Close, cancellationToken);
        var opened = await Execute(connection, Sql.Open, cancellationToken, ("release", release));
        var report = new Dictionary<string, object>
        {
            ["files"] = files.Select(file => file.Id).ToArray(),
            ["rows"] = counts.Rows,
            ["invalidCui"] = counts.InvalidCui,
            ["joinedRecords"] = counts.JoinedRecords,
            ["repairedSeparators"] = counts.RepairedSeparators,
            ["malformedRecords"] = counts.MalformedRecords,
            ["windows1250Lines"] = counts.Windows1250Lines,
            ["undecodableLines"] = counts.UndecodableLines,
            ["namesWithLostLetters"] = await Scalar(connection, "SELECT count(*) FROM fiscal_merged WHERE strpos(name, U&'\\FFFD') > 0", cancellationToken),
            ["placeholderPhones"] = counts.PlaceholderPhones,
            ["unrecognisedFlags"] = counts.UnrecognisedFlags,
            ["unparsedDates"] = counts.UnparsedDates,
            ["taxpayers"] = await Scalar(connection, "SELECT count(*) FROM fiscal_merged", cancellationToken),
            ["duplicateRows"] = await Scalar(connection, "SELECT count(*) - count(DISTINCT cui) FROM fiscal_rows", cancellationToken),
            ["kindsStated"] = stated,
            ["kindsInferred"] = kinds - stated,
            ["kindsUnknown"] = await Scalar(connection, "SELECT count(*) FROM fiscal_merged WHERE kind IS NULL", cancellationToken),
            ["withoutContacts"] = withoutContacts,
            ["latestProcessingDate"] = await Text(connection, "SELECT max(last_processed)::text FROM fiscal_merged", cancellationToken),
            ["periodsExtended"] = extended,
            ["periodsOpened"] = opened,
        };
        database.FiscalReleases.Add(new FiscalRelease
        {
            ReleaseDate = release,
            ImportedAt = time.GetUtcNow(),
            ParserVersion = ParserVersion,
            Report = JsonSerializer.Serialize(report),
        });
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ImportResult(ImportOutcome.Published, 0, report);
    }

    /// <summary>Deletes the fiscal history and the taxpayer types so releases can be applied again from the oldest.</summary>
    public async Task ClearAsync(CancellationToken cancellationToken) =>
        await database.Database.ExecuteSqlRawAsync(
            "TRUNCATE fiscal.taxpayer_periods, fiscal.releases, fiscal.taxpayer_kinds", cancellationToken);

    /// <summary>Reads one pinned file, or the single entry of a pinned zip, after checking its size and hash.</summary>
    private static async IAsyncEnumerable<TaxpayerRow> ReadFileAsync(
        CatalogFile file, string rawRoot, IdentificationReadCounts counts, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var path = Path.Combine(rawRoot, file.LocalPath!);
        if (!file.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            await using var stream = VerifiedFileStream.Open(path, file);
            await foreach (var row in MfIdentificationReader.ReadAsync(stream, counts, cancellationToken))
            {
                yield return row;
            }

            stream.EnsureComplete();
            yield break;
        }

        // A zip is read through its central directory, so the hash is checked in a pass of its own first.
        await using (var verified = VerifiedFileStream.Open(path, file))
        {
            await verified.CopyToAsync(Stream.Null, cancellationToken);
            verified.EnsureComplete();
        }

        using var archive = ZipFile.OpenRead(path);
        var entries = archive.Entries.Where(entry => entry.Length > 0).ToList();
        if (entries.Count != 1)
        {
            throw new InvalidDataException($"{file.FileName}: expected one data file in the archive, found {entries.Count}.");
        }

        await using var entryStream = entries[0].Open();
        await foreach (var row in MfIdentificationReader.ReadAsync(entryStream, counts, cancellationToken))
        {
            yield return row;
        }
    }

    private static async Task WriteRowAsync(NpgsqlBinaryImporter writer, TaxpayerRow row, CancellationToken cancellationToken)
    {
        await writer.StartRowAsync(cancellationToken);
        await writer.WriteAsync(row.Cui, NpgsqlDbType.Bigint, cancellationToken);
        await Nullable(writer, row.Name, cancellationToken);
        await Nullable(writer, NameKey(row.Name), cancellationToken);
        await Nullable(writer, row.Kind, cancellationToken);
        await Nullable(writer, row.UnitType, cancellationToken);
        await Nullable(writer, row.ParentCui, NpgsqlDbType.Bigint, cancellationToken);
        await Nullable(writer, row.County, cancellationToken);
        await Nullable(writer, row.Locality, cancellationToken);
        await Nullable(writer, row.Sector, cancellationToken);
        await Nullable(writer, Key(row.County, row.Locality, row.Sector), cancellationToken);
        await Nullable(writer, row.Street, cancellationToken);
        await Nullable(writer, row.StreetNumber, cancellationToken);
        await Nullable(writer, Key(row.Street, row.StreetNumber), cancellationToken);
        await Nullable(writer, row.Block, cancellationToken);
        await Nullable(writer, row.Staircase, cancellationToken);
        await Nullable(writer, row.Floor, cancellationToken);
        await Nullable(writer, row.Apartment, cancellationToken);
        await Nullable(writer, row.AddressDetails, cancellationToken);
        await Nullable(writer, row.PostalCode, cancellationToken);
        await Nullable(writer, row.Phone, cancellationToken);
        await Nullable(writer, row.Fax, cancellationToken);
        await Nullable(writer, row.RegistrationNumber, cancellationToken);
        await Nullable(writer, row.AuthorizationAct, cancellationToken);
        await Nullable(writer, row.VatPayer, NpgsqlDbType.Boolean, cancellationToken);
        await Nullable(writer, row.VatSince, NpgsqlDbType.Date, cancellationToken);
        await Nullable(writer, row.FiscalStatus, cancellationToken);
        await Nullable(writer, row.StatusDate, NpgsqlDbType.Date, cancellationToken);
        await Nullable(writer, row.DeregistrationDate, NpgsqlDbType.Date, cancellationToken);
        await Nullable(writer, row.LastDeclaration, NpgsqlDbType.Date, cancellationToken);
        await Nullable(writer, row.LastProcessed, NpgsqlDbType.Date, cancellationToken);
        await writer.WriteAsync(row.Obligations, NpgsqlDbType.Array | NpgsqlDbType.Integer, cancellationToken);
        if (row.ObligationDates is { } dates)
        {
            await writer.WriteAsync(dates, NpgsqlDbType.Jsonb, cancellationToken);
        }
        else
        {
            await writer.WriteNullAsync(cancellationToken);
        }
    }

    /// <summary>Folded parts joined by '|': spelling, case and diacritics do not count as a change; null when all are missing.</summary>
    private static string? Key(params string?[] parts) =>
        parts.All(part => part is null) ? null : string.Join('|', parts.Select(part => part is null ? "" : SirutaSeatMatcher.Fold(Marked(part))));

    /// <summary>
    /// Marks letters a release lost: the 2024 file has U+FFFD where ă, â, î, ș and ț were. Folding would turn it into a
    /// space, so it becomes <see cref="LostLetter"/> (a letter folding keeps) and comparisons let it match any one letter.
    /// </summary>
    [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(value))]
    internal static string? Marked(string? value) => value?.Replace('\uFFFD', LostLetter);

    private const char LostLetter = 'Ж';

    /// <summary>
    /// The registry name key, with legal forms recognised even when their letters were lost
    /// ("PERSOANЖ FIZICЖ AUTORIZATЖ" is PFA, as the intact spelling is).
    /// </summary>
    internal static string? NameKey(string? name)
    {
        var key = ImportRegistryHistory.NameKey(Marked(name));
        if (key is null || !key.Contains(LostLetter, StringComparison.Ordinal))
        {
            return key;
        }

        foreach (var (pattern, form) in DamagedForms)
        {
            key = pattern.Replace(key, form);
        }

        return key;
    }

    private static readonly (System.Text.RegularExpressions.Regex Pattern, string Form)[] DamagedForms =
    [
        (new(@"\bPERSOAN[AЖ] FIZIC[AЖ] AUTORIZAT[AЖ]\b", System.Text.RegularExpressions.RegexOptions.CultureInvariant), "PFA"),
        (new(@"\b[IЖ]NTREPRINDERE INDIVIDUAL[AЖ]\b", System.Text.RegularExpressions.RegexOptions.CultureInvariant), "II"),
        (new(@"\b[IЖ]NTREPRINDERE FAMILIAL[AЖ]\b", System.Text.RegularExpressions.RegexOptions.CultureInvariant), "IF"),
    ];

    private static Task Nullable(NpgsqlBinaryImporter writer, string? value, CancellationToken cancellationToken) =>
        value is null ? writer.WriteNullAsync(cancellationToken) : writer.WriteAsync(value, NpgsqlDbType.Text, cancellationToken);

    private static Task Nullable<T>(NpgsqlBinaryImporter writer, T? value, NpgsqlDbType type, CancellationToken cancellationToken)
        where T : struct =>
        value is { } present ? writer.WriteAsync(present, type, cancellationToken) : writer.WriteNullAsync(cancellationToken);

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

    private static async Task<string> Text(NpgsqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 0 };
        return await command.ExecuteScalarAsync(cancellationToken) as string ?? "";
    }

    private static class Sql
    {
        private const string Columns = """
            cui, name, name_key, kind, unit_type, parent_cui, county, locality, sector, place_key, street, street_number, street_key,
            block, staircase, floor, apartment, address_details, postal_code, phone, fax, registration_number, authorization_act,
            vat_payer, vat_since, fiscal_status, status_date, deregistration_date, last_declaration, last_processed, obligations,
            obligation_dates
            """;

        public const string CreateStaging = """
            CREATE TEMP TABLE fiscal_rows (
                cui bigint NOT NULL, name text, name_key text, kind text, unit_type text, parent_cui bigint, county text, locality text,
                sector text, place_key text, street text, street_number text, street_key text, block text, staircase text, floor text,
                apartment text, address_details text, postal_code text, phone text, fax text, registration_number text,
                authorization_act text, vat_payer boolean, vat_since date, fiscal_status text, status_date date, deregistration_date date,
                last_declaration date, last_processed date, obligations integer[] NOT NULL, obligation_dates jsonb
            ) ON COMMIT DROP;
            """;

        public const string Copy = $"COPY fiscal_rows ({Columns}) FROM STDIN (FORMAT BINARY)";

        // One row per CUI: the most recently processed, then the most complete.
        public const string Merge = """
            CREATE TEMP TABLE fiscal_merged ON COMMIT DROP AS
            SELECT DISTINCT ON (r.cui) r.*, false AS extended
            FROM fiscal_rows r
            ORDER BY r.cui, r.last_processed DESC NULLS LAST, num_nonnulls(r.name, r.locality, r.street, r.phone, r.registration_number) DESC;
            CREATE INDEX ON fiscal_merged (cui);
            ANALYZE fiscal_merged;
            """;

        public const string InferKinds = """
            UPDATE fiscal_merged m
            SET kind = coalesce(
                (SELECT k.kind FROM fiscal.taxpayer_kinds k WHERE k.cui = m.cui),
                CASE WHEN m.registration_number LIKE 'F%' THEN 'PF' WHEN m.registration_number ~ '^[JC]' THEN 'PJ' END)
            WHERE m.kind IS NULL
            """;

        public const string DropPersonalFields = """
            UPDATE fiscal_merged
            SET street = NULL, street_number = NULL, street_key = NULL, block = NULL, staircase = NULL, floor = NULL, apartment = NULL,
                address_details = NULL, phone = NULL, fax = NULL
            WHERE kind IS DISTINCT FROM 'PJ'
            """;

        // Keys are equal, or one has lost letters (Ж) that match any single character of the other.
        private static string KeyMatches(string column) => $"""
            (p.{column} IS NOT DISTINCT FROM m.{column}
             OR (strpos(m.{column}, '{LostLetter}') > 0 AND p.{column} LIKE replace(m.{column}, '{LostLetter}', '_'))
             OR (strpos(p.{column}, '{LostLetter}') > 0 AND m.{column} LIKE replace(p.{column}, '{LostLetter}', '_')))
            """;

        private static readonly string Same = $"""
            {KeyMatches("name_key")}
            AND (p.kind IS NULL OR m.kind IS NULL OR p.kind = m.kind)
            AND (p.unit_type IS NULL OR m.unit_type IS NULL OR p.unit_type = m.unit_type)
            AND {KeyMatches("place_key")}
            AND {KeyMatches("street_key")}
            AND p.phone IS NOT DISTINCT FROM m.phone
            AND p.fax IS NOT DISTINCT FROM m.fax
            AND (p.registration_number IS NULL OR m.registration_number IS NULL OR p.registration_number = m.registration_number)
            AND p.vat_payer IS NOT DISTINCT FROM m.vat_payer
            AND p.fiscal_status IS NOT DISTINCT FROM m.fiscal_status
            AND p.status_date IS NOT DISTINCT FROM m.status_date
            AND p.deregistration_date IS NOT DISTINCT FROM m.deregistration_date
            AND p.obligations = m.obligations
            """;

        // The newer spelling, unless it lost letters (U+FFFD) that the period still has.
        private static string Better(string column) =>
            $"CASE WHEN strpos(m.{column}, U&'\\FFFD') > 0 AND p.{column} IS NOT NULL THEN p.{column} ELSE m.{column} END";

        // Extended taxpayers are flagged in fiscal_merged, so Close and Open need no anti-join on the periods table.
        public static readonly string Extend = $"""
            WITH extended AS (
            UPDATE fiscal.taxpayer_periods p
            SET valid_to = @release, releases = p.releases + 1, name = {Better("name")}, county = {Better("county")},
                locality = {Better("locality")}, street = {Better("street")}, street_number = m.street_number, kind = coalesce(m.kind, p.kind), unit_type = coalesce(m.unit_type, p.unit_type),
                parent_cui = coalesce(m.parent_cui, p.parent_cui), registration_number = coalesce(m.registration_number, p.registration_number), block = coalesce(m.block, p.block), staircase = coalesce(m.staircase, p.staircase),
                floor = coalesce(m.floor, p.floor), apartment = coalesce(m.apartment, p.apartment),
                address_details = coalesce(m.address_details, p.address_details), postal_code = coalesce(m.postal_code, p.postal_code),
                authorization_act = coalesce(m.authorization_act, p.authorization_act), vat_since = coalesce(p.vat_since, m.vat_since),
                last_declaration = coalesce(m.last_declaration, p.last_declaration), last_processed = coalesce(m.last_processed, p.last_processed),
                obligation_dates = coalesce(p.obligation_dates, m.obligation_dates)
            FROM fiscal_merged m
            WHERE p.cui = m.cui AND p.latest AND {Same}
            RETURNING p.cui)
            UPDATE fiscal_merged m SET extended = true FROM extended e WHERE m.cui = e.cui
            """;

        public const string Close = """
            UPDATE fiscal.taxpayer_periods p SET latest = false
            FROM fiscal_merged m
            WHERE p.cui = m.cui AND p.latest AND NOT m.extended
            """;

        public const string Open = $"""
            INSERT INTO fiscal.taxpayer_periods (valid_from, valid_to, latest, releases, {Columns})
            SELECT @release, @release, true, 1, {Columns}
            FROM fiscal_merged m
            WHERE NOT m.extended
            """;
    }
}
