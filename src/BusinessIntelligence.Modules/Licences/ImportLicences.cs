using System.IO.Compression;
using System.Text;
using System.Text.Json;
using BusinessIntelligence.Modules.Platform.Files;
using BusinessIntelligence.Modules.Platform.Persistence;
using BusinessIntelligence.Modules.Procurement;
using BusinessIntelligence.Modules.Registry;
using BusinessIntelligence.Modules.Registry.Onrc;
using BusinessIntelligence.Modules.Registry.Sources;
using BusinessIntelligence.Modules.Search;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace BusinessIntelligence.Modules.Licences;

/// <summary>
/// Imports regulator lists into <c>licences.licences</c>, one transaction per file; a newer list of the same kind and
/// name replaces the older one. ANRSC: CSV with a title block, region rows and an operator code made of the county
/// letters and the CUI. ANRM: the attribute table (.dbf) of a shapefile zip. CNAS: old binary .xls workbooks.
/// </summary>
public sealed class ImportLicences(AppDbContext database, TimeProvider time)
{
    public const string AnrscKind = "licences-anrsc";
    public const string AnrmKind = "licences-anrm";
    public const string CnasKind = "licences-cnas";
    public const string ParserVersion = "licences-v1";

    public static IReadOnlyList<CatalogFile> Files() =>
        [.. SourceCatalog.All.Where(file => file.Kind is AnrscKind or AnrmKind or CnasKind).OrderBy(file => file.Id, StringComparer.Ordinal)];

    public async Task<ImportResult> ExecuteAsync(string path, CatalogFile file, CancellationToken cancellationToken)
    {
        if (file.Kind is not (AnrscKind or AnrmKind or CnasKind))
        {
            throw new ArgumentException($"Catalog entry {file.Id} is not a licence list.", nameof(file));
        }

        var existing = await database.SourceFiles.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Sha256 == file.Sha256 && item.ParserVersion == ParserVersion, cancellationToken);
        if (existing is not null)
        {
            return new ImportResult(ImportOutcome.AlreadyImported, existing.Id, new Dictionary<string, object>());
        }

        await using (var verified = VerifiedFileStream.Open(path, file))
        {
            verified.EnsureComplete();
        }

        // Read fully first: the lists are small.
        var rows = file.Kind switch
        {
            AnrscKind => await AnrscAsync(path, file, cancellationToken),
            AnrmKind => Anrm(path),
            _ => await CnasAsync(path, cancellationToken),
        };
        if (rows.Count == 0)
        {
            throw new InvalidDataException($"{file.FileName}: no licence rows were found.");
        }

        var counts = new Dictionary<string, object> { ["rows"] = rows.Count, ["withCui"] = 0, ["withheld"] = 0 };
        database.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));
        await database.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)database.Database.GetDbConnection();
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var source = RegistryImports.NewSourceFile(file, ParserVersion, time, "{}");
        database.SourceFiles.Add(source);
        await database.SaveChangesAsync(cancellationToken);
        await using (var writer = await connection.BeginBinaryImportAsync("""
            COPY licences.licences (source_file_id, row_number, authority, status, holder_name, holder_name_key, holder_cui, activity, details, place)
            FROM STDIN (FORMAT BINARY)
            """, cancellationToken))
        {
            foreach (var row in rows)
            {
                var (state, cui) = ImportSeap.Identifier(row.TaxCode);
                var withheld = state == CuiState.PersonalCode;
                counts["withCui"] = (int)counts["withCui"] + (cui is null ? 0 : 1);
                counts["withheld"] = (int)counts["withheld"] + (withheld ? 1 : 0);
                var name = withheld ? null : row.Holder;
                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(source.Id, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(row.Row, NpgsqlDbType.Integer, cancellationToken);
                await Text(row.Authority);
                await Text(row.Status);
                await Text(name);
                await Text(name is null ? null : SearchKeys.Name(name));
                if (cui is { } value)
                {
                    await writer.WriteAsync(value, NpgsqlDbType.Bigint, cancellationToken);
                }
                else
                {
                    await writer.WriteNullAsync(cancellationToken);
                }

                await Text(row.Activity);
                await Text(row.Details);
                await Text(row.Place);
            }

            await writer.CompleteAsync(cancellationToken);

            Task Text(string? value) => ImportOnrcFirme.WriteAsync(writer, value, cancellationToken);
        }

        var previous = await RegistryImports.PublishAsync(database, file.Kind + "-" + ListName(file), source.Id, time, cancellationToken);
        if (previous is { } old && old != source.Id)
        {
            await ImportOnrcFirme.ExecuteAsync(connection, "DELETE FROM licences.licences WHERE source_file_id = @old", cancellationToken, ("old", old));
        }

        source.Report = JsonSerializer.Serialize(counts);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ImportResult(ImportOutcome.Published, source.Id, counts);
    }

    /// <summary>The list a file belongs to, without its date: a newer edition of the same list replaces it.</summary>
    public static string ListName(CatalogFile file) =>
        System.Text.RegularExpressions.Regex.Replace(file.FileName.ToLowerInvariant(), @"[-_ ]?\d{2}\.\d{2}\.\d{4}|\.(csv|xls|xlsx|zip)$", string.Empty);

    private sealed record LicenceRow(int Row, string Authority, string Status, string? Holder, string? TaxCode, string? Activity, string? Details, string? Place);

    private static async Task<List<LicenceRow>> AnrscAsync(string path, CatalogFile file, CancellationToken cancellationToken)
    {
        var status = file.FileName.Contains("retrase", StringComparison.OrdinalIgnoreCase) ? "retrasă"
            : file.FileName.Contains("respinse", StringComparison.OrdinalIgnoreCase) ? "respinsă" : "valabilă";
        var rows = new List<LicenceRow>();
        var header = false;
        var number = 0;
        string? region = null;
        await using var stream = File.OpenRead(path);
        await foreach (var fields in DelimitedReader.ReadAsync(stream, ',', new UTF8Encoding(false, true), cancellationToken))
        {
            number++;
            string? Field(int index) => index < fields.Length && !string.IsNullOrWhiteSpace(fields[index]) ? Clean(fields[index]) : null;
            if (!header)
            {
                header = Field(0) == "Nr.crt." && Field(1) == "Cod operator";
                continue;
            }

            if (Field(1) is not { } code)
            {
                // A region heading ("EUROREGIUNEA NORD – EST (Botoşani; …)").
                region = Field(0);
                continue;
            }

            // The operator code is the county's car-plate letters followed by the CUI.
            var digits = new string([.. code.SkipWhile(char.IsLetter)]);
            rows.Add(new LicenceRow(number, "ANRSC", status, Field(2), digits, Field(3), Field(4), region));
        }

        return header ? rows : throw new InvalidDataException($"{file.FileName}: header row not found.");
    }

    private static List<LicenceRow> Anrm(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var dbf = archive.Entries.SingleOrDefault(entry => entry.FullName.EndsWith(".dbf", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("The shapefile zip has no .dbf attribute table.");
        using var stream = dbf.Open();
        var rows = new List<LicenceRow>();
        foreach (var (row, record) in Dbf.Read(stream, Encoding.UTF8))
        {
            string? Field(string name) => record.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;
            var perimeter = string.Join(" · ", new[] { Field("NUME_PERIM"), Field("TIP_CONTR1"), Field("NUMAR_TOPO") is { } topo ? "nr. " + topo : null }
                .Where(item => item is not null));
            rows.Add(new LicenceRow(row, "ANRM", "activă", Field("AGENT_ECON"), null, Field("SUBSTANTE"), perimeter,
                string.Join(", ", new[] { Field("LOCALIZARE"), Field("JUDETE") }.Where(item => item is not null))));
        }

        return rows;
    }

    private static async Task<List<LicenceRow>> CnasAsync(string path, CancellationToken cancellationToken)
    {
        var rows = new List<LicenceRow>();
        string[]? header = null;
        await foreach (var (row, cells) in Spreadsheet.ReadAsync(path, 12, cancellationToken))
        {
            if (header is null)
            {
                header = [.. cells.Select(cell => cell?.Trim() ?? string.Empty)];
                if (Array.IndexOf(header, "Cod fiscal furnizor") < 0 || Array.IndexOf(header, "Nume furnizor") < 0)
                {
                    throw new InvalidDataException("Unexpected CNAS header: " + string.Join(" | ", header));
                }

                continue;
            }

            string? Cell(string name) => Array.IndexOf(header, name) is var index and >= 0 && !string.IsNullOrWhiteSpace(cells[index]) ? Clean(cells[index]!) : null;
            if (Cell("Nume furnizor") is null && Cell("Cod fiscal furnizor") is null)
            {
                continue;
            }

            var details = string.Join(" · ", new[] { Cell("Numar contract") is { } contract ? "contract " + contract : null, Cell("Nume tip contract") }
                .Where(item => item is not null));
            rows.Add(new LicenceRow(row, "CNAS", "contract", Cell("Nume furnizor"), Cell("Cod fiscal furnizor"), Cell("Tip furnizor"), details, Cell("Nume CAS")));
        }

        return rows;
    }

    private static string Clean(string value) => System.Text.RegularExpressions.Regex.Replace(value, @"\s+", " ").Trim();
}
