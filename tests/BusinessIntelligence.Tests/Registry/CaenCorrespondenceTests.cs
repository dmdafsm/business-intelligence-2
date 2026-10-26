using System.IO.Compression;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using BusinessIntelligence.Modules.Registry;
using BusinessIntelligence.Modules.Registry.Geography;
using BusinessIntelligence.Modules.Registry.Sources;
using BusinessIntelligence.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BusinessIntelligence.Tests.Registry;

public sealed class CaenCorrespondenceTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IDisposable
{
    private readonly DirectoryInfo _files = Directory.CreateTempSubdirectory("bi-caen-");

    public void Dispose() => _files.Delete(recursive: true);

    // The INS layout: a title, a header on row 6, a split Rev.2 class (A merged over its rows), an aggregation (C merged
    // over three Rev.2 rows), an intermediate class marked "*", and the stray "0" row the September 2026 file ends with.
    private static readonly (int Row, string[] Cells)[] Rows =
    [
        (1, ["Tabel corespondență CAEN Rev.2 - CAEN Rev.3"]),
        (6, ["Clasă CAEN Rev.2", "Denumire clasă  CAEN Rev.2", "Clasă CAEN Rev.3", "Denumire clasă  CAEN Rev.3", "OBS."]),
        (7, ["0111", "Cultivarea cerealelor", "0111", "Cultivarea cerealelor"]),
        (8, ["0119", "Cultivarea altor plante nepermanente", "0113", "Cultivarea legumelor"]),
        (9, ["", "", "0119", "Cultivarea altor plante din culturi nepermanente"]),
        (10, ["3101", "Fabricarea de mobilă pentru birouri", "3100", "Fabricarea de mobilă"]),
        (11, ["3102", "Fabricarea de mobilă pentru bucătării", "", ""]),
        (12, ["3109", "Fabricarea de mobilă n.c.a.", "", ""]),
        (13, ["6201", "Realizarea soft-ului la comandă", "6210*", "Realizarea softului la comandă"]),
        (15, ["0", "", "4689", "Comerţ cu ridicata"]),
    ];

    [Fact]
    public void MergedCellsCountForEveryRowTheySpan()
    {
        var workbook = Workbook(Rows, ["A8:A9", "C10:C12", "D10:D12"]);
        var (pairs, defects) = ImportReferenceData.ParseCaenCorrespondence(SimpleXlsx.ReadFirstSheet(workbook, fillMergedCells: true));

        Assert.Equal(
            [("0111", "0111", false), ("0119", "0113", false), ("0119", "0119", false), ("3101", "3100", false), ("3102", "3100", false),
             ("3109", "3100", false), ("6201", "6210", true)],
            pairs.Select(pair => (pair.Rev2Code, pair.Rev3Code, pair.Intermediate)));
        Assert.Equal("row 15: Rev.2 cell '0' without a name, next to '4689', skipped", Assert.Single(defects));

        // Without filling, the aggregated rows have no Rev.3 class: the reason the import asks for merged cells.
        var unfilled = ImportReferenceData.ParseCaenCorrespondence(SimpleXlsx.ReadFirstSheet(workbook)).Pairs;
        Assert.DoesNotContain(unfilled, pair => pair.Rev2Code == "3102");
    }

    [Fact]
    public async Task TheTableImportsOnceAndReplacesThePreviousOne()
    {
        await using var database = await postgres.CreateIsolatedContextAsync();
        var bytes = Workbook(Rows, ["A8:A9", "C10:C12"]);
        var path = Path.Combine(_files.FullName, "corespondenta.xlsx");
        await File.WriteAllBytesAsync(path, bytes);
        var file = new CatalogFile("caen-test", ImportReferenceData.CorrespondenceKind, "Corespondență test", "corespondenta.xlsx",
            new Uri("https://example.test/corespondenta.xlsx"), "INS", "OGL-ROU-1.0", new DateOnly(2026, 9, 1), bytes.Length,
            Convert.ToHexStringLower(SHA256.HashData(bytes)));
        var import = new ImportReferenceData(database, TimeProvider.System);

        var result = await import.CaenCorrespondenceAsync(path, file, CancellationToken.None);
        Assert.Equal(ImportOutcome.Published, result.Outcome);
        Assert.Equal(7, result.Report["pairs"]);
        Assert.Equal(ImportOutcome.AlreadyImported, (await import.CaenCorrespondenceAsync(path, file, CancellationToken.None)).Outcome);
        Assert.Equal(7, await database.CaenCorrespondence.CountAsync());
    }

    /// <summary>A minimal XLSX with inline strings at the given row numbers and merged ranges.</summary>
    private static byte[] Workbook((int Row, string[] Cells)[] rows, string[] merges)
    {
        var sheet = new StringBuilder("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
        foreach (var (row, cells) in rows)
        {
            sheet.Append("<row r=\"").Append(row).Append("\">");
            for (var column = 0; column < cells.Length; column++)
            {
                if (cells[column].Length > 0)
                {
                    sheet.Append("<c r=\"").Append((char)('A' + column)).Append(row).Append("\" t=\"inlineStr\"><is><t>")
                        .Append(SecurityElement.Escape(cells[column])).Append("</t></is></c>");
                }
            }

            sheet.Append("</row>");
        }

        sheet.Append("</sheetData><mergeCells>");
        foreach (var merge in merges)
        {
            sheet.Append("<mergeCell ref=\"").Append(merge).Append("\"/>");
        }

        sheet.Append("</mergeCells></worksheet>");
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        {
            using var writer = new StreamWriter(archive.CreateEntry("xl/worksheets/sheet1.xml").Open(), new UTF8Encoding(false));
            writer.Write(sheet.ToString());
        }

        return buffer.ToArray();
    }
}
