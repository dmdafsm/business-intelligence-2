using System.Security.Cryptography;
using System.Text;
using BusinessIntelligence.Modules.Financials;
using BusinessIntelligence.Modules.Registry;
using BusinessIntelligence.Modules.Registry.Sources;
using BusinessIntelligence.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BusinessIntelligence.Tests.Financials;

public sealed class FinancialsTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IDisposable
{
    // The 2016+ UU layout: 20 indicators, without public patrimony.
    private const string UuDictionary = """
        CUI;cui;
        CAEN;caen;
        ACTIVE IMOBILIZATE - TOTAL;i1;
        ACTIVE CIRCULANTE - TOTAL, DIN CARE:;i2;
        STOCURI;i3;
        CREANTE;i4;
        CASA SI CONTURI LA BANCI;i5;
        CHELTUIELI IN AVANS;i6;
        DATORII;i7;
        VENITURI IN AVANS;i8;
        PROVIZIOANE;i9;
        CAPITALURI - TOTAL, DIN CARE:;i10;
        CAPITAL SUBSCRIS VARSAT;i11;
        PATRIMONIUL REGIEI;i12;
        CIFRA DE AFACERI NETA;i13;
        VENITURI TOTALE;i14;
        CHELTUIELI TOTALE;i15;
        PROFIT BRUT;i16;
        PIERDERE BRUTA;i17;
        PROFIT NET;i18;
        PIERDERE  NETA;i19;
        NUMAR MEDIU DE SALARIATI;i20;
        """;

    private const string Header = "CUI,CAEN,I1,I2,I3,I4,I5,I6,I7,I8,I9,I10,I11,I12,I13,I14,I15,I16,I17,I18,I19,I20";

    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("bi-mf-");

    public void Dispose() => _root.Delete(recursive: true);

    [Fact]
    public void LabelsDecideTheIndicatorEvenWhenPositionsShift()
    {
        var (columns, corrections) = MfIndicatorMap.Parse(Encoding.UTF8.GetBytes(UuDictionary));
        Assert.Equal(20, columns.Count);
        Assert.Equal(nameof(FinancialStatement.Turnover), columns[12]);
        Assert.Equal(nameof(FinancialStatement.Employees), columns[19]);
        Assert.Empty(corrections);
    }

    [Fact]
    public void MislabelledGrossLossAndUnprefixedKeysAreCorrectedAndReported()
    {
        var dictionary = UuDictionary.Replace("PIERDERE BRUTA;i17;", "PIERDERE NETA;i17;", StringComparison.Ordinal)
            .Replace("PATRIMONIUL REGIEI;i12;", "PATRIMONIUL REGIEI;12;", StringComparison.Ordinal);
        var (columns, corrections) = MfIndicatorMap.Parse(Encoding.UTF8.GetBytes(dictionary));
        Assert.Equal(nameof(FinancialStatement.GrossLoss), columns[16]);
        Assert.Equal(nameof(FinancialStatement.NetLoss), columns[18]);
        Assert.Equal(2, corrections.Count);
    }

    [Fact]
    public void Windows1250DictionariesAreRead()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var bytes = Encoding.GetEncoding(1250).GetBytes(UuDictionary.Replace("LA BANCI", "LA BĂNCI", StringComparison.Ordinal));
        Assert.Equal(nameof(FinancialStatement.Cash), MfIndicatorMap.Parse(bytes).Columns[4]);
    }

    [Theory]
    [InlineData("CIFRA DE AFACERI NETA;i13;", "CIFRA MISTERIOASA;i13;")]
    [InlineData("NUMAR MEDIU DE SALARIATI;i20;", "NUMAR MEDIU DE SALARIATI;i21;")]
    [InlineData("PROFIT NET;i18;", "PROFIT BRUT;i18;")]
    public void UnknownLabelsGapsAndRepeatsAreRejected(string original, string replacement)
    {
        var dictionary = UuDictionary.Replace(original, replacement, StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => MfIndicatorMap.Parse(Encoding.UTF8.GetBytes(dictionary)));
    }

    [Fact]
    public async Task StatementsKeepBlankApartFromZeroAndPadCaen()
    {
        await using var database = await postgres.CreateIsolatedContextAsync();
        var (data, dictionary) = Write(2024, "UU", "v1",
            "10960940,111,100,200,,0,-5,,300,,,-1000,200,,1500,1600,1700,0,100,0,100,3",
            "478197,6201,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1",
            "478197,6201,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1",
            "8629511,4120,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,999999999999999",
            "7937734,4120,1,1,1,1,1,1,1,1,1,1,1,1,300000000000000,1,1,1,1,1,1,2000",
            "3474203,4120,1,1,1,1,1,1,1,1,1,1,1,1,1000,1,1,1,1,1,1,1500");

        var result = await new ImportMfFinancials(database, TimeProvider.System).ExecuteAsync(data, dictionary, _root.FullName, CancellationToken.None);

        Assert.Equal(ImportOutcome.Published, result.Outcome);
        Assert.Equal(1, result.Report["duplicateRows"]);
        var statement = await database.FinancialStatements.AsNoTracking().SingleAsync(item => item.Cui == 10960940);
        Assert.Equal(("0111", (short?)2, "UU"), (statement.CaenCode, statement.CaenVersion, statement.Family));
        Assert.Null(statement.Inventories);
        Assert.Equal(0m, statement.Receivables);
        Assert.Equal(-5m, statement.Cash);
        Assert.Equal(-1000m, statement.Equity);
        Assert.Equal(1500m, statement.Turnover);
        Assert.Null(statement.PublicPatrimony);
        Assert.Equal((0m, 100m, 0m, 100m), (statement.GrossProfit!.Value, statement.GrossLoss!.Value, statement.NetProfit!.Value, statement.NetLoss!.Value));
        Assert.Equal(3, statement.Employees);
        Assert.Equal(1L, result.Report["implausibleEmployeeCounts"]);
        var filler = await database.FinancialStatements.AsNoTracking().SingleAsync(item => item.Cui == 8629511);
        Assert.Null(filler.Employees);
        Assert.Equal(["employees_out_of_range"], filler.QualityFlags);

        // A 300 trillion turnover is stored as missing; 2,000 employees are kept but flagged once turnover is gone.
        var huge = await database.FinancialStatements.AsNoTracking().SingleAsync(item => item.Cui == 7937734);
        Assert.Null(huge.Turnover);
        Assert.Equal(2000, huge.Employees);
        Assert.Equal(["amount_out_of_range"], huge.QualityFlags);
        Assert.Empty(statement.QualityFlags);
        Assert.Equal(1L, result.Report["implausibleAmountRows"]);
        var unlikely = await database.FinancialStatements.AsNoTracking().SingleAsync(item => item.Cui == 3474203);
        Assert.Equal(1500, unlikely.Employees);
        Assert.Equal(["employees_unlikely"], unlikely.QualityFlags);
        Assert.Equal(5, await database.FinancialStatements.CountAsync());
    }

    [Fact]
    public async Task ARevisedFileReplacesItsYearAndFamilyOnly()
    {
        await using var database = await postgres.CreateIsolatedContextAsync();
        var import = new ImportMfFinancials(database, TimeProvider.System);
        var (other, otherDictionary) = Write(2023, "UU", "other", "478197,6201,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1");
        await import.ExecuteAsync(other, otherDictionary, _root.FullName, CancellationToken.None);
        var (first, dictionary) = Write(2024, "UU", "first", "10960940,6201,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1");
        await import.ExecuteAsync(first, dictionary, _root.FullName, CancellationToken.None);
        Assert.Equal(ImportOutcome.AlreadyImported, (await import.ExecuteAsync(first, dictionary, _root.FullName, CancellationToken.None)).Outcome);

        var (revised, revisedDictionary) = Write(2024, "UU", "revised",
            "10960940,6201,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2",
            "8629511,4120,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1");
        var result = await import.ExecuteAsync(revised, revisedDictionary, _root.FullName, CancellationToken.None);

        Assert.Equal(1, result.Report["replacedStatements"]);
        Assert.Equal(2m, (await database.FinancialStatements.AsNoTracking().SingleAsync(item => item.Cui == 10960940 && item.Year == 2024)).Turnover);
        Assert.Equal(3, await database.FinancialStatements.CountAsync());
        Assert.True(await database.FinancialStatements.AnyAsync(item => item.Year == 2023));
    }

    [Fact]
    public async Task HeaderThatDisagreesWithItsDictionaryChangesNothing()
    {
        await using var database = await postgres.CreateIsolatedContextAsync();
        var (data, dictionary) = Write(2024, "UU", "short-header", ["10960940,6201,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1"],
            Header[..Header.LastIndexOf(',')]);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new ImportMfFinancials(database, TimeProvider.System).ExecuteAsync(data, dictionary, _root.FullName, CancellationToken.None));
        database.ChangeTracker.Clear();
        Assert.Equal(0, await database.SourceFiles.CountAsync());
        Assert.Equal(0, await database.FinancialStatements.CountAsync());
    }

    private (CatalogFile Data, CatalogFile Dictionary) Write(short year, string family, string name, params string[] rows) =>
        Write(year, family, name, rows, Header);

    private (CatalogFile Data, CatalogFile Dictionary) Write(short year, string family, string name, string[] rows, string header)
    {
        var data = Encoding.UTF8.GetBytes(header + "\r\n" + string.Join("\r\n", rows) + "\r\n");
        var dictionary = Encoding.UTF8.GetBytes(UuDictionary);
        File.WriteAllBytes(Path.Combine(_root.FullName, name + ".txt"), data);
        File.WriteAllBytes(Path.Combine(_root.FullName, name + ".csv"), dictionary);
        return (Entry(name + "-data", ImportMfFinancials.DataKind, name + ".txt", data, year, family),
            Entry(name + "-dictionary", ImportMfFinancials.DictionaryKind, name + ".csv", dictionary, year, family));
    }

    private static CatalogFile Entry(string id, string kind, string path, byte[] content, short year, string family) =>
        new(id, kind, "Test", path, new Uri("https://example.test/" + path), "Test", "CC BY 4.0", new DateOnly(year, 12, 31),
            content.Length, Convert.ToHexStringLower(SHA256.HashData(content)), year, family, path);
}
