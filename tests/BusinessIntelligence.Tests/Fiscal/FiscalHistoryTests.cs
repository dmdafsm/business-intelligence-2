using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using BusinessIntelligence.Modules.Fiscal;
using BusinessIntelligence.Modules.Registry;
using BusinessIntelligence.Modules.Registry.Sources;
using BusinessIntelligence.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BusinessIntelligence.Tests.Fiscal;

public sealed class FiscalHistoryTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IDisposable
{
    // Source layouts as published: 2018–2021 columns from the MF explicatii.csv, 2022 on from the file header.
    private static readonly string[] CsvColumns =
    [
        "cod_fiscal", "nume", "loc", "str", "nr", "di", "dp", "fax", "sect", "tel", "jud_com", "nr_com", "an_com", "act_aut", "tva",
        "sfarsit", "cp", "data_stare", "stare", "judet", "imp_100", "imp_120", "imp_200", "imp_410", "imp_416", "imp_420", "imp_423",
        "imp_430", "imp_439", "imp_500", "imp_602", "imp_701", "imp_710", "imp_755", "imp_756", "bilanturi", "imp_412", "imp_480",
        "imp_432", "detalii_adresa", "bloc", "scara", "etaj", "ap",
    ];

    private static readonly string[] DelimitedColumns =
    [
        "COD_FISCAL", "DENUMIRE", "COD_FISCAL_PARINTE", "TIP_UNITATE", "TIP_CONTRIB", "LOCALITATE", "STRADA", "NR", "DATA_INREGISTRARE",
        "DATA_PRELUCRARE", "FAX", "SECTOR", "TELEFON", "JUDET_COMERT", "NR_COMERT", "AN_COMERT", "ACT_AUTORIZARE", "TVA", "DATA_RADIERE",
        "COD_POSTAL", "DATA_STARE", "STARE", "JUDET", "IMP100", "IMP120", "IMP130", "IMP160", "ACCIZE200", "CONT410", "CONT412", "CONT416",
        "CONT420", "CONT423", "CONT430", "CONT432", "IMP439", "IMP451", "IMP461", "IMP473", "IMP474", "CONT480", "IMP500", "IMP602",
        "IMP611", "IMP625", "IMP632", "IMP633", "IMP634", "IMP701", "IMP710", "IMP711", "IMP712", "IMP713", "IMP755", "IMP756", "IMP940",
        "DETALII_ADRESA", "BLOC", "SCARA", "ETAJ", "AP",
    ];

    private static readonly DateOnly First = new(2018, 8, 9);
    private static readonly DateOnly Second = new(2021, 6, 18);
    private static readonly DateOnly Third = new(2022, 6, 21);
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("bi-fiscal-");

    public void Dispose() => _root.Delete(recursive: true);

    [Fact]
    public async Task HeaderlessCsvIsReadByPosition()
    {
        var counts = new IdentificationReadCounts();
        var rows = await ReadAsync(Encoding.UTF8.GetBytes(string.Join("\r\n",
            Csv(39, new()
            {
                ["cod_fiscal"] = "10960940",
                ["nume"] = "EXEMPLU \"\"NORD\"\", SRL",
                ["loc"] = "Cluj-Napoca",
                ["str"] = "Str. A",
                ["nr"] = "2",
                ["di"] = "03 Martie     2015",
                ["dp"] = "02 Ianuarie   2018",
                ["tel"] = "0264-123456",
                ["fax"] = "0",
                ["jud_com"] = "J12",
                ["nr_com"] = "0001",
                ["an_com"] = "2001",
                ["tva"] = "01-07-2003",
                ["sfarsit"] = "null",
                ["cp"] = "12345",
                ["data_stare"] = "01 Iulie      2001",
                ["stare"] = "INREGISTRAT",
                ["judet"] = "CLUJ",
                ["imp_100"] = "01-01-2003",
                ["imp_602"] = "DA",
            }),
            Csv(39, new() { ["cod_fiscal"] = "10960941", ["nume"] = "INVALID CUI" }),
            Csv(38, new() { ["cod_fiscal"] = "478197" })) + "\r\n"), counts);

        var row = Assert.Single(rows);
        Assert.Equal((10960940L, "EXEMPLU \"NORD\", SRL", "J12/1/2001"), (row.Cui, row.Name, row.RegistrationNumber));
        Assert.Equal((new DateOnly(2015, 3, 3), new DateOnly(2018, 1, 2)), (row.LastDeclaration!.Value, row.LastProcessed!.Value));
        Assert.Equal(("0264123456", (string?)null, "012345"), (row.Phone, row.Fax, row.PostalCode));
        Assert.Equal((true, new DateOnly(2003, 7, 1)), (row.VatPayer!.Value, row.VatSince!.Value));
        Assert.Null(row.DeregistrationDate);
        Assert.Equal([100, 602], row.Obligations);
        Assert.Equal("{\"100\":\"2003-01-01\"}", row.ObligationDates);
        Assert.Equal((2, 1, 1, 1), (counts.Rows, counts.InvalidCui, counts.MalformedRecords, counts.PlaceholderPhones));
    }

    [Fact]
    public async Task DelimitedRecordsAreRepaired()
    {
        var whole = Delimited('^', new()
        {
            ["COD_FISCAL"] = "10960940",
            ["DENUMIRE"] = "EXEMPLU SRL",
            ["TIP_CONTRIB"] = "PJ",
            ["STRADA"] = "Str. A",
            ["TVA"] = "DA ",
            ["DATA_INREGISTRARE"] = "14/Jul/23",
            ["DATA_PRELUCRARE"] = "14-Jul-2024 8:19:39 PM",
            ["DATA_STARE"] = "31.01.1991",
        });
        var split = Delimited('^', new() { ["COD_FISCAL"] = "478197", ["DENUMIRE"] = "SPLIT SRL", ["TIP_CONTRIB"] = "PJ", ["LOCALITATE"] = "Blejoi", ["STRADA"] = "Str. Lungă" });
        var at = split.IndexOf("Lun", StringComparison.Ordinal);
        var extra = Delimited('^', new() { ["COD_FISCAL"] = "8629511", ["DENUMIRE"] = "PIPE SRL", ["TIP_CONTRIB"] = "PJ", ["STRADA"] = "Str. Nouă^ Str. Veche", ["NR"] = "5" });
        var encoding = CodePages();
        byte[] content =
        [
            .. Encoding.UTF8.GetBytes(string.Join('^', DelimitedColumns) + "\r\n" + whole + "\r\n" + split[..at] + "\r\n" + split[at..] + "\r\n"),
            .. encoding.GetBytes(extra + "\r\n"),
        ];
        var counts = new IdentificationReadCounts();
        var rows = await ReadAsync(content, counts);

        Assert.Equal([10960940L, 478197L, 8629511L], rows.Select(row => row.Cui));
        Assert.Equal((true, (DateOnly?)null), (rows[0].VatPayer!.Value, rows[0].VatSince));
        Assert.Equal((new DateOnly(2023, 7, 14), new DateOnly(2024, 7, 14), new DateOnly(1991, 1, 31)),
            (rows[0].LastDeclaration!.Value, rows[0].LastProcessed!.Value, rows[0].StatusDate!.Value));
        Assert.Equal(0, counts.UnparsedDates);
        Assert.Equal(("Blejoi", "Str. Lungă"), (rows[1].Locality, rows[1].Street));
        Assert.Equal(("Str. Nouă^ Str. Veche", "5"), (rows[2].Street, rows[2].StreetNumber));
        Assert.Equal((1, 1, 1), (counts.JoinedRecords, counts.RepairedSeparators, counts.Windows1250Lines));
    }

    [Fact]
    public async Task PeriodsSpanMissingReleasesAndIndividualsKeepNoContacts()
    {
        await using var database = await postgres.CreateIsolatedContextAsync();
        var import = new ImportFiscalHistory(database, TimeProvider.System);

        // 2018: a company (J reference), an individual known only from 2022 (PF), a sole trader (F reference).
        var first = CsvFile(First,
            Csv(39, Company("Str. Lungă")),
            Csv(39, new() { ["cod_fiscal"] = "478197", ["nume"] = "ION MARIA PFA", ["loc"] = "Ploieşti", ["str"] = "Str. B", ["tel"] = "0244123456", ["judet"] = "PRAHOVA", ["stare"] = "INREGISTRAT" }),
            Csv(39, new() { ["cod_fiscal"] = "8629511", ["nume"] = "POPESCU PFA", ["loc"] = "Arad", ["str"] = "Str. C", ["tel"] = "0257123456", ["jud_com"] = "F2", ["nr_com"] = "7", ["an_com"] = "2010", ["judet"] = "ARAD", ["stare"] = "INREGISTRAT" }));
        // 2021 (zipped, with a quoted header): the company with the street respelled; the others are missing.
        var second = ZipFile(Second, string.Join(',', CsvColumns.Select(column => '"' + column + '"')) + "\r\n" + Csv(44, Company("STR. LUNGA")));
        // 2022: the company changed its phone; the individual is listed again, now with its type and letters lost (U+FFFD).
        var thirdRows = new[]
        {
            Delimited('|', new() { ["COD_FISCAL"] = "10960940", ["DENUMIRE"] = "EXEMPLU SRL", ["TIP_CONTRIB"] = "PJ", ["LOCALITATE"] = "Cluj-Napoca", ["STRADA"] = "Str. Lungă", ["TELEFON"] = "0264999999", ["JUDET_COMERT"] = "J12", ["NR_COMERT"] = "1", ["AN_COMERT"] = "2001", ["STARE"] = "INREGISTRAT", ["JUDET"] = "CLUJ", ["IMP100"] = "DA " }),
            Delimited('|', new() { ["COD_FISCAL"] = "478197", ["DENUMIRE"] = "ION MARIA PERSOAN\uFFFD FIZIC\uFFFD AUTORIZAT\uFFFD", ["TIP_CONTRIB"] = "PF", ["LOCALITATE"] = "Ploie\uFFFDti", ["STRADA"] = "Str. B", ["TELEFON"] = "0244123456", ["STARE"] = "INREGISTRAT", ["JUDET"] = "PRAHOVA" }),
        };
        var third = DelimitedFile(Third, '|', thirdRows);

        Assert.Equal(2, await import.BuildKindsAsync(third, _root.FullName, CancellationToken.None));
        foreach (var (release, files) in new[] { (First, first), (Second, second), (Third, third) })
        {
            Assert.Equal(ImportOutcome.Published, (await import.ExecuteAsync(release, files, _root.FullName, CancellationToken.None)).Outcome);
        }

        var periods = await database.TaxpayerPeriods.AsNoTracking().OrderBy(item => item.Cui).ThenBy(item => item.ValidFrom).ToListAsync();
        var company = periods.Where(item => item.Cui == 10960940).ToList();
        Assert.Equal([(First, Second, (short)2, false), (Third, Third, (short)1, true)],
            company.Select(item => (item.ValidFrom, item.ValidTo, item.Releases, item.Latest)));
        Assert.Equal(("PJ", "0264123456", "0264999999"), (company[0].Kind, company[0].Phone, company[1].Phone));
        Assert.Equal("{\"100\": \"2003-01-01\"}", company[0].ObligationDates);

        var individual = Assert.Single(periods, item => item.Cui == 478197);
        Assert.Equal((First, Third, (short)2, "PF"), (individual.ValidFrom, individual.ValidTo, individual.Releases, individual.Kind));
        Assert.Equal(("Ploieşti", (string?)null, (string?)null), (individual.Locality, individual.Street, individual.Phone));

        var soleTrader = Assert.Single(periods, item => item.Cui == 8629511);
        Assert.Equal(("PF", "F2/7/2010", (string?)null, (string?)null), (soleTrader.Kind, soleTrader.RegistrationNumber, soleTrader.Street, soleTrader.Phone));

        Assert.Equal(ImportOutcome.AlreadyImported, (await import.ExecuteAsync(Third, third, _root.FullName, CancellationToken.None)).Outcome);
        await Assert.ThrowsAsync<InvalidOperationException>(() => import.ExecuteAsync(Second, second, _root.FullName, CancellationToken.None));
        await import.ClearAsync(CancellationToken.None);
        Assert.Equal((0, 0), (await database.TaxpayerPeriods.CountAsync(), await database.TaxpayerKinds.CountAsync()));
    }

    [Fact]
    public async Task ChangedFileIsRefused()
    {
        await using var database = await postgres.CreateIsolatedContextAsync();
        var files = CsvFile(First, Csv(39, Company("Str. A")));
        await File.AppendAllTextAsync(Path.Combine(_root.FullName, files[0].LocalPath!), "x");
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new ImportFiscalHistory(database, TimeProvider.System).ExecuteAsync(First, files, _root.FullName, CancellationToken.None));
        Assert.Equal(0, await database.FiscalReleases.CountAsync());
    }

    private static Dictionary<string, string> Company(string street) => new()
    {
        ["cod_fiscal"] = "10960940",
        ["nume"] = "EXEMPLU SRL",
        ["loc"] = "Cluj-Napoca",
        ["str"] = street,
        ["tel"] = "0264123456",
        ["jud_com"] = "J12",
        ["nr_com"] = "1",
        ["an_com"] = "2001",
        ["stare"] = "INREGISTRAT",
        ["judet"] = "CLUJ",
        ["imp_100"] = "01-01-2003",
    };

    private static string Csv(int width, Dictionary<string, string> values) =>
        string.Join(',', CsvColumns[..width].Select(column =>
            values.TryGetValue(column, out var value) ? '"' + value + '"'
            : column.StartsWith("imp_", StringComparison.Ordinal) || column == "tva" ? "NU" : ""));

    private static string Delimited(char delimiter, Dictionary<string, string> values) =>
        string.Join(delimiter, DelimitedColumns.Select(column =>
            values.TryGetValue(column, out var value) ? value
            : column.StartsWith("IMP", StringComparison.Ordinal) || column.StartsWith("CONT", StringComparison.Ordinal)
                || column.StartsWith("ACCIZE", StringComparison.Ordinal) || column == "TVA" ? "NU " : "")) + delimiter;

    private List<CatalogFile> CsvFile(DateOnly release, params string[] rows) =>
        Pin(release, ".csv", Encoding.UTF8.GetBytes(string.Join("\r\n", rows) + "\r\n"));

    private List<CatalogFile> DelimitedFile(DateOnly release, char delimiter, string[] rows) =>
        Pin(release, ".txt", Encoding.UTF8.GetBytes(string.Join(delimiter, DelimitedColumns) + "\r\n" + string.Join("\r\n", rows) + "\r\n"));

    private List<CatalogFile> ZipFile(DateOnly release, string content)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var entry = archive.CreateEntry("date.csv").Open();
            entry.Write(Encoding.UTF8.GetBytes(content));
        }

        return Pin(release, ".zip", buffer.ToArray());
    }

    private List<CatalogFile> Pin(DateOnly release, string extension, byte[] bytes)
    {
        var name = $"{release:yyyyMMdd}-{Guid.NewGuid():N}{extension}";
        File.WriteAllBytes(Path.Combine(_root.FullName, name), bytes);
        return [new CatalogFile(name, ImportFiscalHistory.Kind, "Test", name, new Uri("https://example.test/" + name), "Test", "CC BY 4.0",
            release, bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes)), (short)release.Year, LocalPath: name)];
    }

    private static Encoding CodePages()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1250);
    }

    private static async Task<List<TaxpayerRow>> ReadAsync(byte[] content, IdentificationReadCounts counts)
    {
        var rows = new List<TaxpayerRow>();
        await foreach (var row in MfIdentificationReader.ReadAsync(new MemoryStream(content), counts, CancellationToken.None))
        {
            rows.Add(row);
        }

        return rows;
    }
}
