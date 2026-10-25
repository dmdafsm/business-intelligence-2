using System.IO.Compression;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using BusinessIntelligence.Modules.Platform.Files;
using BusinessIntelligence.Modules.Procurement;
using BusinessIntelligence.Modules.Profile;
using BusinessIntelligence.Modules.Registry;
using BusinessIntelligence.Modules.Registry.Sources;
using BusinessIntelligence.Tests.Infrastructure;
using BusinessIntelligence.Tests.Search;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BusinessIntelligence.Tests.Procurement;

public sealed class SeapImportTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IDisposable
{
    private readonly DirectoryInfo _files = Directory.CreateTempSubdirectory("bi-seap-");

    public void Dispose() => _files.Delete(recursive: true);

    [Theory]
    [InlineData("RO 10960940", CuiState.Valid, 10960940L)]
    [InlineData("10960940.0", CuiState.Valid, 10960940L)]
    [InlineData("1850101123456", CuiState.PersonalCode, null)]
    [InlineData("1.850101123456E+12", CuiState.PersonalCode, null)]
    [InlineData("ABC", CuiState.NonNumeric, null)]
    public void IdentifiersAreReadAsSeapWritesThem(string text, CuiState state, long? cui)
    {
        Assert.Equal((state, cui), ImportSeap.Identifier(text));
    }

    [Fact]
    public async Task WorkbookRowsComeBackByColumnWithSharedAndInlineStrings()
    {
        var path = Workbook("read", [["A", "B", "C"], ["x", null, "45931.5"]], shared: true);
        var rows = new List<(int Row, string?[] Cells)>();
        await foreach (var row in StreamingXlsx.ReadAsync(path, 3, CancellationToken.None))
        {
            rows.Add(row);
        }

        Assert.Equal("A|B|C", string.Join('|', rows[0].Cells));
        Assert.Equal(("x", (string?)null, "45931.5"), (rows[1].Cells[0], rows[1].Cells[1], rows[1].Cells[2]));
        Assert.Equal(new DateOnly(2025, 10, 1), StreamingXlsx.SerialDate(rows[1].Cells[2]));
    }

    [Fact]
    public async Task ContractsKeepEveryWinnerCountEachContractOnceAndWithholdPersonalCodes()
    {
        await using var database = await postgres.CreateIsolatedContextAsync();
        await SearchFixtures.SeedAsync(database, build: false);
        var soft = SearchFixtures.ExempluSoft.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string?[] Contract(string notice, string value, string winner, string winnerCui) =>
            ["Primaria Test", "4305857", "Anunt", "Licitatie deschisa", "1", "45900", "L98", "Contract", "Furnizare", "Pret",
                "30200000-1", "Echipament informatic", "1", notice, "45931", "45930", "C-1", "SEAP", value, winner, winnerCui];

        var q3 = Write("seap-contracts", 3, [ImportSeap.ContractHeader, ["DENUMIRE_AC", null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
            Contract("A1", "1000.5", "EXEMPLU SOFT SRL", "RO" + soft),
            Contract("A1", "1000.5", "PARTENER SRL", "478197"),
            Contract("A2", "200", "POPESCU ION", "1850101123456")]);
        // The same contract A1 is published again in the next quarter.
        var q4 = Write("seap-contracts", 4, [ImportSeap.ContractHeader, Contract("A1", "1000.5", "EXEMPLU SOFT SRL", soft)]);
        var import = new ImportSeap(database, TimeProvider.System);

        var result = await import.ExecuteAsync(q3.Path, q3.File, CancellationToken.None);
        await import.ExecuteAsync(q4.Path, q4.File, CancellationToken.None);
        Assert.Equal(1, result.Report["withheldWinners"]);
        Assert.Equal(ImportOutcome.AlreadyImported, (await import.ExecuteAsync(q3.Path, q3.File, CancellationToken.None)).Outcome);

        var rows = await database.ProcurementAwards.AsNoTracking().Where(item => item.Quarter == 3).OrderBy(item => item.RowNumber).ToListAsync();
        Assert.Equal([(short)2, (short)2, (short)1], rows.Select(item => item.Winners));
        Assert.Equal([true, false, true], rows.Select(item => item.FirstWinner));
        Assert.Equal((null, null, "PersonalCode"), (rows[2].WinnerName, rows[2].WinnerCui, rows[2].WinnerCuiState));
        Assert.Equal((new DateOnly(2025, 9, 30), 1000.5m), (rows[0].ContractDate!.Value, rows[0].ValueRon!.Value));

        var profile = await new CompanyProfileQuery(database).ExecuteAsync(SearchFixtures.ExempluSoft, null, CancellationToken.None);
        // Besides the fixture's one direct purchase (Q1), contract A1 counts once although it has two rows in Q3 and one in Q4.
        var won = Assert.Single(profile!.Procurement!.Won);
        Assert.Equal((2025, 1, 1000.5m, 1), (won.Year, won.Contracts, won.ContractValue, won.DirectPurchases));
        Assert.Equal(2, profile.Procurement.RecentWins.Count);
        Assert.Equal("2025 (T1, T3, T4)", profile.Procurement.Coverage);
    }

    [Fact]
    public async Task DirectPurchasesAndAWrongHeaderIsRefused()
    {
        await using var database = await postgres.CreateIsolatedContextAsync();
        await SearchFixtures.SeedAsync(database, build: false);
        var soft = SearchFixtures.ExempluSoft.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var direct = Write("seap-direct", 1, [ImportSeap.DirectHeader,
            ["Scoala Test", "4305857", "DA1", "45700.25", "Licente software", "48000000-8", "Software", "Furnizare", "Da", "PNRR", "45710", "500", "EXEMPLU SOFT SRL", soft],
            ["Scoala Test", "4305857", "DA2", "45701", "Hartie", "30197630-1", "Hartie", "Furnizare", "Nu", null, "45711", "", "ALTA SRL", "478197"]]);
        var import = new ImportSeap(database, TimeProvider.System);
        var result = await import.ExecuteAsync(direct.Path, direct.File, CancellationToken.None);
        Assert.Equal(1, result.Report["missingValues"]);
        var rows = await database.ProcurementAwards.AsNoTracking().Where(item => item.SourceFileId == result.SourceFileId).OrderBy(item => item.RowNumber).ToListAsync();
        Assert.Equal(("direct", "Licente software", true, "PNRR", 500m), (rows[0].Kind, rows[0].Title!, rows[0].EuFunded!.Value, rows[0].Programme!, rows[0].ValueRon!.Value));
        Assert.Null(rows[1].ValueRon);

        var wrong = Write("seap-direct", 2, [["Autoritate", "CUI"], ["x", "1"]]);
        await Assert.ThrowsAsync<InvalidDataException>(() => import.ExecuteAsync(wrong.Path, wrong.File, CancellationToken.None));
        Assert.Equal(2, await database.ProcurementAwards.CountAsync(item => item.SourceFileId == result.SourceFileId));
    }

    private (string Path, CatalogFile File) Write(string kind, int quarter, IEnumerable<IReadOnlyList<string?>> rows)
    {
        var path = Workbook($"{kind}-{quarter}", rows, shared: false);
        var bytes = File.ReadAllBytes(path);
        var file = new CatalogFile($"{kind}-2025-q{quarter}", kind, "Achiziții publice 2025", Path.GetFileName(path), new Uri("https://example.test/" + kind),
            "ADR", "CC BY 4.0", new DateOnly(2025, quarter * 3, quarter is 1 or 4 ? 31 : 30), bytes.Length,
            Convert.ToHexStringLower(SHA256.HashData(bytes)), 2025, $"Q{quarter}");
        return (path, file);
    }

    /// <summary>A minimal .xlsx: one sheet, strings shared or inline, numbers as numeric cells.</summary>
    private string Workbook(string name, IEnumerable<IReadOnlyList<string?>> rows, bool shared)
    {
        var path = Path.Combine(_files.FullName, name + ".xlsx");
        var strings = new List<string>();
        var sheet = new StringBuilder("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
        var number = 0;
        foreach (var row in rows)
        {
            number++;
            sheet.Append(System.Globalization.CultureInfo.InvariantCulture, $"<row r=\"{number}\">");
            for (var column = 0; column < row.Count; column++)
            {
                var reference = $"{(char)('A' + column % 26)}{(column >= 26 ? "" : "")}{number}";
                if (column >= 26)
                {
                    reference = $"A{(char)('A' + column - 26)}{number}";
                }

                var value = row[column];
                if (value is null)
                {
                    continue;
                }

                if (decimal.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _) && !value.StartsWith('0'))
                {
                    sheet.Append(System.Globalization.CultureInfo.InvariantCulture, $"<c r=\"{reference}\"><v>{value}</v></c>");
                }
                else if (shared)
                {
                    strings.Add(value);
                    sheet.Append(System.Globalization.CultureInfo.InvariantCulture, $"<c r=\"{reference}\" t=\"s\"><v>{strings.Count - 1}</v></c>");
                }
                else
                {
                    sheet.Append(System.Globalization.CultureInfo.InvariantCulture, $"<c r=\"{reference}\" t=\"inlineStr\"><is><t>{SecurityElement.Escape(value)}</t></is></c>");
                }
            }

            sheet.Append("</row>");
        }

        sheet.Append("</sheetData></worksheet>");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        Entry("xl/worksheets/sheet1.xml", sheet.ToString());
        Entry("xl/sharedStrings.xml", "<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">"
            + string.Concat(strings.Select(text => $"<si><t>{SecurityElement.Escape(text)}</t></si>")) + "</sst>");
        return path;

        void Entry(string entryName, string content)
        {
            using var stream = archive.CreateEntry(entryName).Open();
            stream.Write(Encoding.UTF8.GetBytes(content));
        }
    }
}
