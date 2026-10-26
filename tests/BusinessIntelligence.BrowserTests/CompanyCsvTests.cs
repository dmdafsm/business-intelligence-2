using System.Text;
using BusinessIntelligence.Host.Components;
using BusinessIntelligence.Modules.Risk;
using BusinessIntelligence.Modules.Search;
using Xunit;

namespace BusinessIntelligence.BrowserTests;

public sealed class CompanyCsvTests
{
    [Fact]
    public void CellsAreEscapedMissingValuesStayEmptyAndSourcesCloseTheFile()
    {
        var result = new SearchResult
        {
            Ready = true,
            Total = 3,
            Hits =
            [
                new SearchHit("C1", 1, "J1/1/2000", "ALFA; \"BETA\" SRL", "SRL", "Active", new DateOnly(2000, 1, 2), "Cluj-Napoca", "Cluj", false, null,
                    2025, 1_234_567, 0, -50),
                new SearchHit("R2", null, "F2/2/2010", "=SUM(A1) PFA", null, "Active", null, null, null, false, null),
            ],
        };
        var bytes = CompanyCsv.Write(result, new Dictionary<string, string[]> { ["C1"] = [RiskSignals.NegativeEquity, RiskSignals.AnafInactive] },
            value => value == "Active" ? "Activă" : value, new DateOnly(2026, 10, 26));

        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]);
        var lines = Encoding.UTF8.GetString(bytes[3..]).Split("\r\n");
        Assert.Equal(string.Join(',', CompanyCsv.Header), lines[0]);
        // Quotes doubled inside a quoted cell; numbers plain; zero employees kept as 0, a negative result as published.
        Assert.Equal("1,J1/1/2000,\"ALFA; \"\"BETA\"\" SRL\",SRL,Activă,2000-01-02,Cluj-Napoca,Cluj,2025,1234567,0,-50,\"Capitaluri proprii negative, Inactiv fiscal\"", lines[1]);
        // A missing value is an empty cell, never 0; a cell a spreadsheet would run as a formula is neutralized.
        Assert.Equal(",F2/2/2010,'=SUM(A1) PFA,,Activă,,,,,,,,", lines[2]);
        Assert.StartsWith("\"Export din 26.10.2026, primele 2 din 3 rezultate. Surse: Oficiul Național al Registrului Comerțului (CC BY 4.0);", lines[3], StringComparison.Ordinal);
        Assert.Equal(string.Empty, lines[4]);
    }

    [Fact]
    public void TheWorkbookKeepsNumbersAsNumbersAndTheSourcesOnTheirOwnSheet()
    {
        var result = new SearchResult
        {
            Ready = true,
            Total = 1,
            Hits = [new SearchHit("C1", 12345678, "J1/1/2000", "ALFA & BETA <SRL>", "SRL", "Active", new DateOnly(2000, 1, 2), null, "Cluj", false, null, 2025, 1_234_567, null, -50)],
        };
        var bytes = CompanyCsv.WriteXlsx(result, new Dictionary<string, string[]>(), value => "Activă", new DateOnly(2026, 10, 26));
        // The repository's reader parses the first sheet back.
        var rows = BusinessIntelligence.Modules.Registry.Geography.SimpleXlsx.ReadFirstSheet(bytes);

        Assert.Equal(CompanyCsv.Header, rows[0]);
        Assert.Equal(["12345678", "J1/1/2000", "ALFA & BETA <SRL>", "SRL", "Activă", "2000-01-02", "", "Cluj", "2025", "1234567", "", "-50"], rows[1]);
        using var archive = new System.IO.Compression.ZipArchive(new MemoryStream(bytes));
        using var sources = new StreamReader(archive.GetEntry("xl/worksheets/sheet2.xml")!.Open());
        Assert.Contains("Oficiul Național al Registrului Comerțului", sources.ReadToEnd(), StringComparison.Ordinal);
    }
}
