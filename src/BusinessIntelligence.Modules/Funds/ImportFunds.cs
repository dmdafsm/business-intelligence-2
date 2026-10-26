using System.Globalization;
using System.Text;
using System.Text.Json;
using BusinessIntelligence.Modules.Platform.Files;
using BusinessIntelligence.Modules.Platform.Persistence;
using BusinessIntelligence.Modules.Procurement;
using BusinessIntelligence.Modules.Registry;
using BusinessIntelligence.Modules.Registry.Geography;
using BusinessIntelligence.Modules.Registry.Onrc;
using BusinessIntelligence.Modules.Registry.Sources;
using BusinessIntelligence.Modules.Search;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace BusinessIntelligence.Modules.Funds;

/// <summary>
/// Imports a list of EU-funded projects into <c>funds.projects</c>, one transaction per file. A newer list for the same
/// programme replaces the older one. Ministry lists are workbooks whose header row and columns differ by programme: the
/// header is found by its labels (Romanian or English). AFIR lists are CSV files with fixed columns.
/// </summary>
public sealed class ImportFunds(AppDbContext database, TimeProvider time)
{
    public const string MinistryKind = "eu-projects-mipe";
    public const string AfirKind = "eu-projects-afir";
    public const string ParserVersion = "eu-projects-v1";
    private const int Columns = 48;

    public static IReadOnlyList<CatalogFile> Files() =>
        [.. SourceCatalog.All.Where(file => file.Kind is MinistryKind or AfirKind).OrderBy(file => file.Id, StringComparer.Ordinal)];

    /// <summary>The programme a file covers, from its name: POR, POIM, POCU… or PNDR 2007–2013 / 2014–2020.</summary>
    public static string Programme(CatalogFile file) => file.Kind == AfirKind
        ? file.FileName.Contains("2013", StringComparison.Ordinal) ? "PNDR 2007–2013" : "PNDR 2014–2020"
        : file.FileName.Split('-')[0].ToUpperInvariant();

    public async Task<ImportResult> ExecuteAsync(string path, CatalogFile file, CancellationToken cancellationToken)
    {
        if (file.Kind is not (MinistryKind or AfirKind))
        {
            throw new ArgumentException($"Catalog entry {file.Id} is not a list of EU-funded projects.", nameof(file));
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

        var counties = await CountiesAsync(cancellationToken);
        // The AFIR 2007–2013 list has no county column: it comes from the SIRUTA code.
        var countyBySiruta = await database.Siruta.AsNoTracking().Join(database.Siruta.Where(place => place.Level == 1),
                place => place.CountyNumber, county => county.CountyNumber, (place, county) => new { place.Code, county.Name })
            .ToDictionaryAsync(item => item.Code, item => item.Name, cancellationToken);
        var programme = Programme(file);
        var counts = new Dictionary<string, int> { ["rows"] = 0, ["withCui"] = 0, ["withheld"] = 0, ["withoutBeneficiary"] = 0, ["countyUnmatched"] = 0 };

        database.Database.SetCommandTimeout(TimeSpan.FromHours(1));
        await database.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)database.Database.GetDbConnection();
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var source = RegistryImports.NewSourceFile(file, ParserVersion, time, "{}");
        database.SourceFiles.Add(source);
        await database.SaveChangesAsync(cancellationToken);

        await using (var writer = await connection.BeginBinaryImportAsync(Copy, cancellationToken))
        {
            var rows = file.Kind == MinistryKind ? MinistryRowsAsync(path, cancellationToken) : AfirRowsAsync(path, countyBySiruta, cancellationToken);
            await foreach (var row in rows)
            {
                counts["rows"]++;
                if (row.Beneficiary is null)
                {
                    counts["withoutBeneficiary"]++;
                }

                var (state, cui) = ImportSeap.Identifier(row.TaxCode);
                var withheld = state == CuiState.PersonalCode;
                counts["withheld"] += withheld ? 1 : 0;
                counts["withCui"] += cui is null ? 0 : 1;
                short? county = row.County is null ? null : counties.GetValueOrDefault(CountyKey(row.County));
                counts["countyUnmatched"] += row.County is not null && county is null ? 1 : 0;
                var name = withheld ? null : row.Beneficiary;

                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(source.Id, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(row.Row, NpgsqlDbType.Integer, cancellationToken);
                await Text(programme);
                await Text(row.Code);
                await Text(row.Title);
                await Text(name);
                await Text(name is null ? null : SearchKeys.Name(name));
                await Value(cui, NpgsqlDbType.Bigint);
                await Text(row.County);
                await Value(county, NpgsqlDbType.Smallint);
                await Text(row.Locality);
                await Text(row.Status);
                await Value(row.Start, NpgsqlDbType.Date);
                await Value(row.End, NpgsqlDbType.Date);
                await Value(row.Eligible, NpgsqlDbType.Numeric);
                await Value(row.Total, NpgsqlDbType.Numeric);
                await Value(row.Contracted, NpgsqlDbType.Numeric);
                await Value(row.Paid, NpgsqlDbType.Numeric);
            }

            await writer.CompleteAsync(cancellationToken);

            Task Text(string? value) => ImportOnrcCompanies.WriteAsync(writer, value, cancellationToken);

            async Task Value<T>(T? value, NpgsqlDbType type) where T : struct
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
        }

        if (counts["rows"] == 0)
        {
            throw new InvalidDataException($"{file.FileName}: no project rows were found.");
        }

        var previous = await RegistryImports.PublishAsync(database, $"eu-projects-{programme}", source.Id, time, cancellationToken);
        if (previous is { } old && old != source.Id)
        {
            await ImportOnrcCompanies.ExecuteAsync(connection, "DELETE FROM funds.projects WHERE source_file_id = @old", cancellationToken, ("old", old));
        }

        source.Report = JsonSerializer.Serialize(counts);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ImportResult(ImportOutcome.Published, source.Id, counts.ToDictionary(item => item.Key, item => (object)item.Value));
    }

    private const string Copy = """
        COPY funds.projects (source_file_id, row_number, programme, project_code, title, beneficiary_name, beneficiary_name_key, beneficiary_cui,
            county, county_number, locality, status, start_date, end_date, eligible_value, total_value, contracted_value, paid_value) FROM STDIN (FORMAT BINARY)
        """;

    private sealed record ProjectRow(int Row, string? Code, string? Title, string? Beneficiary, string? TaxCode, string? County, string? Locality,
        string? Status, DateOnly? Start, DateOnly? End, decimal? Eligible, decimal? Total, decimal? Contracted, decimal? Paid);

    /// <summary>Header labels (folded, Romanian and English) that identify each column; the left-most match wins.</summary>
    private static readonly (string Field, string[] Labels)[] MinistryColumns =
    [
        ("title", ["TITLU PROIECT", "PROJECT TITLE"]),
        ("beneficiary", ["DENUMIRE BENEFICIAR", "NUME BENEFICIAR", "BENEFICIARY NAME"]),
        ("code", ["COD SMIS", "CODSMIS", "SMIS CODE", "SMIS"]),
        ("start", ["DATA DE INCEPERE", "START DATE"]),
        ("end", ["DATA DE FINALIZARE", "END DATE"]),
        ("county", ["JUDET", "COUNTY"]),
        ("locality", ["LOCALITATE", "LOCALITY"]),
        ("status", ["STADIU PROIECT", "PROJECT STATUS"]),
        ("total", ["TOTAL VALOARE PROIECT", "TOTAL VALUE OF THE PROJECT"]),
        ("eligible", ["VALOAREA ELIGIBILA A PROIECTULUI", "ELIGIBLE VALUE OF THE PROJECT"]),
        ("paid", ["PLATI CATRE BENEFICIARI", "BENEFICIARY PAYMENTS", "PLATI"]),
    ];

    private static async IAsyncEnumerable<ProjectRow> MinistryRowsAsync(string path, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Dictionary<string, int>? columns = null;
        await foreach (var (row, cells) in StreamingXlsx.ReadAsync(path, Columns, cancellationToken))
        {
            if (columns is null)
            {
                if (row > 30)
                {
                    throw new InvalidDataException("No header row with a project title and a beneficiary column in the first 30 rows.");
                }

                var found = new Dictionary<string, int>();
                for (var index = 0; index < cells.Length; index++)
                {
                    var label = cells[index] is { } text ? SirutaSeatMatcher.Fold(text) : null;
                    foreach (var (field, labels) in MinistryColumns.Where(column => !found.ContainsKey(column.Field)))
                    {
                        if (label is not null && labels.Any(prefix => label.StartsWith(prefix, StringComparison.Ordinal)))
                        {
                            found[field] = index;
                            break;
                        }
                    }
                }

                if (found.ContainsKey("title") && found.ContainsKey("beneficiary"))
                {
                    columns = found;
                }

                continue;
            }

            string? Cell(string field) => columns.TryGetValue(field, out var index) && !string.IsNullOrWhiteSpace(cells[index]) ? cells[index]!.Trim() : null;
            if (Cell("title") is not { } title || Cell("beneficiary") is not { } beneficiary)
            {
                // Sub-headers ("Fonduri UE", "Buget național") and blank rows.
                continue;
            }

            yield return new ProjectRow(row, Cell("code"), title, beneficiary, null, Cell("county"), Cell("locality"), Cell("status"),
                Date(Cell("start")), Date(Cell("end")), Amount(Cell("eligible")), Amount(Cell("total")), null, Amount(Cell("paid")));
        }

        if (columns is null)
        {
            throw new InvalidDataException("No header row with a project title and a beneficiary column.");
        }
    }

    private static readonly string[] Afir2013 =
        ["entityid", "nume", "tip_organizare", "cod_fiscal", "roapia", "siruta", "localitate", "adresa", "codproiect", "valoare_contractata",
            "valoare_platita", "oj", "cr", "longitude", "latitude", "amplasarelocalitate"];

    private static readonly string[] Afir2020 =
        ["entityid", "judet", "localitate", "siruta", "ansesiune", "sesiune", "denumirebeneficiar", "titluproiect", "codproiect", "statuscurent",
            "valoare_cf", "valoare_contractata", "valoare_platita", "latitude", "longitude", "zonamontana"];

    // The beneficiary's address (2007–2013) is not stored.
    private static async IAsyncEnumerable<ProjectRow> AfirRowsAsync(string path, Dictionary<int, string> countyBySiruta,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        string[]? header = null;
        var row = 1;
        await foreach (var fields in DelimitedReader.ReadAsync(stream, ',', new UTF8Encoding(false, true), cancellationToken))
        {
            if (header is null)
            {
                header = fields;
                if (!header.SequenceEqual(Afir2013) && !header.SequenceEqual(Afir2020))
                {
                    throw new InvalidDataException("Unexpected AFIR header: " + string.Join(',', header));
                }

                continue;
            }

            row++;
            if (fields.Length != header.Length)
            {
                throw new InvalidDataException($"Row {row}: expected {header.Length} fields, found {fields.Length}.");
            }

            string? Field(string name) => Array.IndexOf(header, name) is var index and >= 0 && !string.IsNullOrWhiteSpace(fields[index]) ? fields[index].Trim() : null;
            var siruta = int.TryParse(Field("siruta"), NumberStyles.None, CultureInfo.InvariantCulture, out var code) ? code : (int?)null;
            var county = Field("judet") ?? (siruta is { } place ? countyBySiruta.GetValueOrDefault(place) : null);
            yield return new ProjectRow(row, Field("codproiect")?.TrimStart('\''), Field("titluproiect"), Field("denumirebeneficiar") ?? Field("nume"),
                Field("cod_fiscal"), county, Field("localitate"), Field("statuscurent"), null, null, null, null,
                Amount(Field("valoare_contractata")), Amount(Field("valoare_platita")));
        }
    }

    private async Task<Dictionary<string, short>> CountiesAsync(CancellationToken cancellationToken)
    {
        var counties = new Dictionary<string, short>(StringComparer.Ordinal);
        foreach (var county in await database.Siruta.AsNoTracking().Where(place => place.Level == 1).ToListAsync(cancellationToken))
        {
            counties[CountyKey(county.Name)] = county.CountyNumber;
        }

        return counties;
    }

    /// <summary>A county name compared without "JUDEȚUL"/"MUNICIPIUL", diacritics, case and punctuation.</summary>
    public static string CountyKey(string name)
    {
        var key = SirutaSeatMatcher.Fold(name);
        foreach (var prefix in new[] { "JUDETUL ", "JUDET ", "MUNICIPIUL " })
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal))
            {
                key = key[prefix.Length..];
            }
        }

        return key.Replace(" ", string.Empty, StringComparison.Ordinal);
    }

    private static DateOnly? Date(string? text) =>
        StreamingXlsx.SerialDate(text)
        ?? (DateOnly.TryParseExact(text?.Split(' ')[0], ["dd.MM.yyyy", "d.M.yyyy", "dd/MM/yyyy", "yyyy-MM-dd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date : null);

    private static decimal? Amount(string? text) =>
        decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? Math.Round(value, 2)
        : decimal.TryParse(text, NumberStyles.Number, CultureInfo.GetCultureInfo("ro-RO"), out var local) ? Math.Round(local, 2)
        : null;
}
