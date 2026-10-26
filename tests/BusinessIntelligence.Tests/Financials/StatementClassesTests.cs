using BusinessIntelligence.Modules.Financials;
using BusinessIntelligence.Modules.Profile;
using BusinessIntelligence.Modules.Registry;
using BusinessIntelligence.Tests.Infrastructure;
using BusinessIntelligence.Tests.Search;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BusinessIntelligence.Tests.Financials;

public sealed class StatementClassesTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly string[] Rev3Codes = ["1111", "2222", "3333", "4444", "5555"];

    [Fact]
    public async Task OpenVersionStatementsGetARev3ClassOnlyWhenOneFollows()
    {
        await using var database = await postgres.CreateIsolatedContextAsync();
        await SearchFixtures.SeedAsync(database, build: false);
        // Rev.3 classes 1111–5555 and a correspondence where 9002, 2222 and 5555 split, and 3333 means 4444 in Rev.2.
        database.Caen.AddRange(Rev3Codes.Select(code => new CaenEntry { Version = 3, Code = code, Level = "class", Section = "X", Name = "Clasa " + code }));
        database.CaenCorrespondence.AddRange(
            Pair("9001", "1111"), Pair("9002", "1111"), Pair("9002", "2222"), Pair("3333", "4444"), Pair("5555", "5555"), Pair("5555", "1111"),
            Pair("2222", "2222"), Pair("2222", "3333"));
        var filler = SearchFixtures.Cui(60001);
        database.CompanyActivities.AddRange(
            Activity("C" + SearchFixtures.ExemplulNou, "2222"), Activity("C" + filler, "2222"), Activity("C" + filler, "3333"));
        database.SourceFiles.Add(new SourceFile
        {
            Id = 125,
            CatalogId = "test-mf-2025",
            Kind = "mf-financials-data",
            Dataset = "Situații financiare 2025",
            FileName = "web2025.txt",
            Url = "https://example.invalid/web2025.txt",
            Publisher = "Ministerul Finanțelor",
            License = "CC BY 4.0",
            ReferenceDate = new DateOnly(2025, 12, 31),
            Sha256 = new string('9', 64),
            Bytes = 1,
            ParserVersion = "test",
            ImportedAt = DateTimeOffset.UnixEpoch,
            Report = "{}",
        });
        await database.SaveChangesAsync();
        database.PublishedDatasets.Add(new PublishedDataset { Kind = "mf-financials-2025-UU", SourceFileId = 125, PublishedAt = DateTimeOffset.UnixEpoch });
        database.FinancialStatements.AddRange(
            Statement(SearchFixtures.ExempluSoft, "9001"),   // only Rev.2: one class
            Statement(SearchFixtures.ExemplulNou, "9002"),   // Rev.2 split; the registry has one of the two
            Statement(SearchFixtures.AlfaExemplu, "3333"),   // both versions; the registry is still Rev.2, where 3333 means 4444
            Statement(SearchFixtures.Zarzavat, "5555"),      // both versions, no registry activities: left open
            Statement(filler, "2222"));                      // both; the registry has 2222 and 3333, the declared code is one of them
        await database.SaveChangesAsync();

        var result = await new BuildFinancialBenchmarks(database, TimeProvider.System).ExecuteAsync(CancellationToken.None);
        var classes = await database.FinancialStatements.AsNoTracking().Where(item => item.Year == 2025)
            .ToDictionaryAsync(item => item.Cui, item => (item.ClassVersion, item.ClassCode, item.ClassMethod));

        Assert.Equal(((short?)3, "1111", StatementClasses.OnlyCandidate), classes[SearchFixtures.ExempluSoft]);
        Assert.Equal(((short?)3, "2222", StatementClasses.Registry), classes[SearchFixtures.ExemplulNou]);
        Assert.Equal(((short?)3, "4444", StatementClasses.RegistryRev2), classes[SearchFixtures.AlfaExemplu]);
        Assert.Equal(((short?)null, (string?)null, (string?)null), classes[SearchFixtures.Zarzavat]);
        Assert.Equal(((short?)3, "2222", StatementClasses.SameCode), classes[filler]);
        Assert.Equal(1, result.Classes["none"]);

        // Published versions are never rewritten; benchmarks group 2025 by the derived class.
        Assert.True(await database.FinancialStatements.AllAsync(item => item.Year == 2025 || item.ClassMethod == null));
        Assert.True(await database.FinancialStatements.AnyAsync(item => item.Year == 2025 && item.CaenCode == "9001" && item.CaenVersion == null));
        Assert.True(await database.FinancialBenchmarks.AnyAsync(item => item.Year == 2025 && item.CaenVersion == 3 && item.CaenCode == "1111"));
        Assert.False(await database.FinancialBenchmarks.AnyAsync(item => item.Year == 2025 && item.CaenCode == "5555"));

        // The profile compares on 2025 in the derived class and says how it was set.
        var profile = await new CompanyProfileQuery(database).ExecuteAsync(SearchFixtures.ExempluSoft, null, CancellationToken.None);
        var comparison = profile!.Comparison!;
        Assert.Equal((2025, (short)3, "1111", StatementClasses.OnlyCandidate, "9001", "Clasa 1111"),
            (comparison.Year, comparison.CaenVersion, comparison.CaenCode, comparison.ClassMethod, comparison.DeclaredCode, comparison.CaenName));
    }

    private static CaenCorrespondence Pair(string rev2, string rev3) => new() { Rev2Code = rev2, Rev3Code = rev3 };

    private static CompanyActivity Activity(string key, string code) =>
        new() { SnapshotId = SearchFixtures.Snapshot, CompanyKey = key, CaenVersion = 3, CaenCode = code };

    private static FinancialStatement Statement(long cui, string code) => new()
    {
        Year = 2025,
        Cui = cui,
        Family = "UU",
        CaenCode = code,
        CaenVersion = null,
        SourceFileId = 125,
        Turnover = 1_000,
        NetProfit = 100,
        Employees = 2,
    };
}
