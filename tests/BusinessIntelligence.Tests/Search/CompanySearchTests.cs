using BusinessIntelligence.Modules.Financials;
using BusinessIntelligence.Modules.Registry.Onrc;
using BusinessIntelligence.Modules.Search;
using BusinessIntelligence.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BusinessIntelligence.Tests.Search;

public sealed class CompanySearchTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Theory]
    [InlineData("RO 10960940", 10960940L)]
    [InlineData(" 10960940 ", 10960940L)]
    [InlineData("10960941", null)]
    [InlineData("1850101123456", null)]
    public void OnlyValidCuisAreIdentifiers(string text, long? cui)
    {
        Assert.Equal(cui, SearchKeys.Parse(text).Cui);
    }

    [Theory]
    [InlineData("J08/0005/2010", "J8/5/2010")]
    [InlineData("j 8 / 5 / 2010", "J8/5/2010")]
    [InlineData("J2024000100123", "J2024000100123")]
    [InlineData("Exemplu SRL", null)]
    public void RegistryNumbersAreNormalized(string text, string? key)
    {
        Assert.Equal(key, SearchKeys.RegistrationNumber(text));
    }

    [Fact]
    public void NameKeysDropPunctuationSocietatePrefixAndLegalFormWords()
    {
        var query = SearchKeys.Parse("S.C. Alfa-Exemplu S.R.L.");
        Assert.Equal("ALFA EXEMPLU SRL", query.NameKey);
        Assert.Equal(["ALFA", "EXEMPLU"], query.Words);
    }

    [Theory]
    [InlineData("MUNICIPIUL CLUJ-NAPOCA", false, "Municipiul Cluj-Napoca")]
    [InlineData("LUNA DE SUS", false, "Luna de Sus")]
    [InlineData("JUDEŢUL ARGEŞ", true, "Argeș")]
    [InlineData("MUNICIPIUL BUCUREŞTI", true, "București")]
    public void PlaceNamesAreShownInSentenceForm(string name, bool county, string expected)
    {
        Assert.Equal(expected, PlaceNames.Display(name, county));
    }

    [Fact]
    public async Task WithoutABuildPagesAreNotReady()
    {
        await using var database = await postgres.CreateIsolatedContextAsync();
        await SearchFixtures.SeedAsync(database, build: false);
        var result = await new CompanySearch(database).ExecuteAsync(new SearchRequest("exemplu"), CancellationToken.None);
        Assert.False(result.Ready);
    }

    [Fact]
    public async Task IdentifiersMatchFirstAndPersonalCodesNever()
    {
        await using var database = await SeededAsync();
        var search = new CompanySearch(database);

        var byCui = await search.ExecuteAsync(new SearchRequest("RO" + SearchFixtures.ExempluSoft), CancellationToken.None);
        var hit = Assert.Single(byCui.Hits);
        Assert.True(hit.IdentifierMatch);
        Assert.Equal("EXEMPLU SOFT SRL", hit.Name);

        // The older registration's number, written with padding, still finds the company (SQL and .NET keys agree).
        var byOldNumber = await search.ExecuteAsync(new SearchRequest("J08/0005/2010"), CancellationToken.None);
        Assert.Equal(SearchFixtures.ExempluSoft, Assert.Single(byOldNumber.Hits).Cui);

        var byPersonalCode = await search.ExecuteAsync(new SearchRequest("1850101123456"), CancellationToken.None);
        Assert.Empty(byPersonalCode.Hits);
        var byNumber = await search.ExecuteAsync(new SearchRequest("F12/5/2010"), CancellationToken.None);
        Assert.Equal("POPESCU ION PFA", Assert.Single(byNumber.Hits).Name);
    }

    [Fact]
    public async Task NamesRankExactThenPrefixThenWordThenApproximate()
    {
        await using var database = await SeededAsync();
        var search = new CompanySearch(database);

        var result = await search.ExecuteAsync(new SearchRequest("exemplu"), CancellationToken.None);
        // Exact; prefix with the active company first; a later word; ALFA's "S.C." prefix does not hide it.
        Assert.Equal(["EXEMPLU", "EXEMPLU SOFT SRL", "EXEMPLUL NOU SRL", "S.C. ALFA EXEMPLU S.R.L."], result.Hits.Select(hit => hit.Name));
        Assert.Equal(4, result.Total);

        var typo = await search.ExecuteAsync(new SearchRequest("exmplu soft"), CancellationToken.None);
        Assert.Equal("EXEMPLU SOFT SRL", typo.Hits[0].Name);

        var earlier = await search.ExecuteAsync(new SearchRequest("vechi nume"), CancellationToken.None);
        var renamed = Assert.Single(earlier.Hits);
        Assert.Equal("EXEMPLU SOFT SRL", renamed.Name);
        Assert.Equal("VECHI NUME S.R.L.", renamed.EarlierName);

        var tooShort = await search.ExecuteAsync(new SearchRequest("e"), CancellationToken.None);
        Assert.True(tooShort.QueryTooShort);
        Assert.Empty(tooShort.Hits);
    }

    [Fact]
    public async Task FiltersCombineAndCountsAgreeBetweenStoredAndLivePaths()
    {
        await using var database = await SeededAsync();
        var search = new CompanySearch(database);

        var national = await search.ExecuteAsync(new SearchRequest(), CancellationToken.None);
        Assert.Equal(SearchFixtures.Filler + 6, national.Total);
        Assert.Equal([("București", SearchFixtures.Filler + 1L), ("Cluj", 5L)], national.Places.Select(item => (item.Label, item.Count)).OrderBy(item => item.Label));
        Assert.Equal([("3:C", 1L), ("3:K", 2L)], national.Activities.Select(item => (item.Value, item.Count)));

        // Stored counts (county, CAEN, lifecycle) against the live path (the same filters plus a locality).
        var stored = await search.ExecuteAsync(new SearchRequest(County: SearchFixtures.Cluj, Caen: "62"), CancellationToken.None);
        Assert.Equal(2, stored.Total);
        Assert.Equal(["K", "62"], stored.CaenPath.Select(node => node.Code));
        Assert.Equal([("3:621", 2L), ("3:622", 1L)], stored.Activities.Select(item => (item.Value, item.Count)));
        Assert.Equal([("Municipiul Cluj-Napoca", 2L)], stored.Places.Select(item => (item.Label, item.Count)));
        Assert.Equal([(Lifecycle.Active, 1L), (Lifecycle.StruckOff, 1L)], stored.Lifecycles.Select(item => (item.Value, item.Count)));
        var live = await search.ExecuteAsync(new SearchRequest(County: SearchFixtures.Cluj, Uat: SearchFixtures.ClujNapoca, Caen: "62"), CancellationToken.None);
        Assert.Equal(stored.Total, live.Total);
        Assert.Equal(stored.Activities, live.Activities);
        Assert.Equal(stored.Lifecycles, live.Lifecycles);

        var active = await search.ExecuteAsync(new SearchRequest(County: SearchFixtures.Cluj, Caen: "62", Lifecycle: Lifecycle.Active), CancellationToken.None);
        Assert.Equal("EXEMPLU SOFT SRL", Assert.Single(active.Hits).Name);
        // The lifecycle counts ignore the lifecycle filter, so the other states stay visible.
        Assert.Equal(2, active.Lifecycles.Count);

        var named = await search.ExecuteAsync(new SearchRequest("exemplu", County: SearchFixtures.Cluj, Lifecycle: Lifecycle.StruckOff), CancellationToken.None);
        Assert.Equal(["EXEMPLU", "EXEMPLUL NOU SRL"], named.Hits.Select(hit => hit.Name));
        Assert.Equal([("3:C", 1L), ("3:K", 1L)], named.Activities.Select(item => (item.Value, item.Count)));

        // Rev.2 is a separate tree: the Rev.2 company is not counted under Rev.3 codes.
        var revision2 = await search.ExecuteAsync(new SearchRequest(Caen: "J", CaenVersion: 2), CancellationToken.None);
        Assert.Equal(SearchFixtures.Filler + 1, revision2.Total);
        var unknown = await search.ExecuteAsync(new SearchRequest(Caen: "6201"), CancellationToken.None);
        Assert.Empty(unknown.CaenPath);
        Assert.Equal(national.Total, unknown.Total);
    }

    [Fact]
    public async Task FinancialFiltersUseTheLatestYearAndNeverReadBlankAsZero()
    {
        await using var database = await postgres.CreateIsolatedContextAsync();
        await SearchFixtures.SeedAsync(database, build: false);
        // ALFA EXEMPLU reports 5,000 employees and no turnover in 2013: a source error kept out of filters and order.
        database.FinancialStatements.Add(new FinancialStatement
        {
            Year = 2013,
            Cui = SearchFixtures.AlfaExemplu,
            Family = "BL",
            SourceFileId = 103,
            Employees = 5_000,
        });
        await database.SaveChangesAsync();
        await new BuildSearchIndex(database, TimeProvider.System).ExecuteAsync(CancellationToken.None);
        var search = new CompanySearch(database);

        // EXEMPLU SOFT filed 2013 with 3 employees and a blank turnover; 2010's turnover does not count for 2013.
        var employees = await search.ExecuteAsync(new SearchRequest(Financials: new FinancialFilter(EmployeesMin: 1)), CancellationToken.None);
        Assert.Equal((short?)2013, employees.FinancialYear);
        var hit = Assert.Single(employees.Hits);
        Assert.Equal((SearchFixtures.ExempluSoft, (short?)2013, (decimal?)null, (int?)3), (hit.Cui!.Value, hit.FinancialYear, hit.Turnover, hit.Employees));
        Assert.Equal(1, employees.Total);

        var turnover = await search.ExecuteAsync(new SearchRequest(Financials: new FinancialFilter(TurnoverMin: 0)), CancellationToken.None);
        Assert.Equal(0, turnover.Total);
        var range = await search.ExecuteAsync(new SearchRequest(Financials: new FinancialFilter(EmployeesMin: 4, EmployeesMax: 10)), CancellationToken.None);
        Assert.Equal(0, range.Total);

        // Ordered by employees, the known count leads and everything else follows by name; nothing is dropped.
        var sorted = await search.ExecuteAsync(new SearchRequest(County: SearchFixtures.Cluj, Financials: new FinancialFilter(Sort: FinancialFilter.ByEmployees)), CancellationToken.None);
        var all = await search.ExecuteAsync(new SearchRequest(County: SearchFixtures.Cluj), CancellationToken.None);
        Assert.Equal(SearchFixtures.ExempluSoft, sorted.Hits[0].Cui);
        Assert.Equal(all.Total, sorted.Total);
        var bucharest = await search.ExecuteAsync(new SearchRequest(County: SearchFixtures.Bucuresti, Financials: new FinancialFilter(EmployeesMin: 1)), CancellationToken.None);
        Assert.Equal(0, bucharest.Total);
    }

    [Fact]
    public async Task FilteredCountsAgreeWithTheTable()
    {
        await using var database = await postgres.CreateIsolatedContextAsync();
        await SearchFixtures.SeedAsync(database);
        var search = new CompanySearch(database);
        var companies = await database.SearchCompanies.AsNoTracking().ToListAsync();

        // A legal-form filter with a lifecycle: the lifecycle facet ignores the chosen lifecycle, every other count applies it.
        var result = await search.ExecuteAsync(new SearchRequest(Lifecycle: Lifecycle.Active, LegalForm: "SRL"), CancellationToken.None);
        var srl = companies.Where(item => item.LegalForm == "SRL").ToList();
        Assert.Equal(srl.Count(item => item.Lifecycle == Lifecycle.Active), result.Total);
        Assert.Equal(srl.GroupBy(item => item.Lifecycle).ToDictionary(group => group.Key, group => (long)group.Count()),
            result.Lifecycles.ToDictionary(item => item.Value, item => item.Count));
        Assert.Equal(
            srl.Where(item => item.Lifecycle == Lifecycle.Active && item.CountyNumber != null).GroupBy(item => item.CountyNumber!.Value)
                .ToDictionary(group => group.Key.ToString(System.Globalization.CultureInfo.InvariantCulture), group => (long)group.Count()),
            result.Places.ToDictionary(item => item.Value, item => item.Count));
        // Sections are counted from the short caen_upper array; each must match the full token list.
        Assert.NotEmpty(result.Activities);
        Assert.All(result.Activities, item => Assert.Equal(srl.Count(company => company.Lifecycle == Lifecycle.Active && company.Caen.Contains(item.Value)), item.Count));
        Assert.All(companies, item => Assert.Subset(item.Caen.ToHashSet(), item.CaenUpper.ToHashSet()));

        // Within a county, the locality counts; in name order, as the stored path would list them.
        var county = await search.ExecuteAsync(new SearchRequest(County: SearchFixtures.Cluj, LegalForm: "SRL"), CancellationToken.None);
        Assert.Equal(srl.Where(item => item.CountyNumber == SearchFixtures.Cluj && item.UatCode != null).GroupBy(item => item.UatCode!.Value)
                .ToDictionary(group => group.Key.ToString(System.Globalization.CultureInfo.InvariantCulture), group => (long)group.Count()),
            county.Places.ToDictionary(item => item.Value, item => item.Count));
        Assert.Equal(county.Hits.Select(hit => hit.Name).Order(StringComparer.Ordinal), county.Hits.Select(hit => hit.Name));
    }

    [Fact]
    public async Task PagesAreServerSideAndBounded()
    {
        await using var database = await SeededAsync();
        var search = new CompanySearch(database);
        var first = await search.ExecuteAsync(new SearchRequest("firma test"), CancellationToken.None);
        var second = await search.ExecuteAsync(new SearchRequest("firma test", Page: 2), CancellationToken.None);
        Assert.Equal(SearchFixtures.Filler, first.Total);
        Assert.Equal(2, first.Pages);
        Assert.Equal(CompanySearch.PageSize, first.Hits.Count);
        Assert.Equal(SearchFixtures.Filler - CompanySearch.PageSize, second.Hits.Count);
        Assert.Empty(first.Hits.Select(hit => hit.CompanyKey).Intersect(second.Hits.Select(hit => hit.CompanyKey)));

        var browse = await search.ExecuteAsync(new SearchRequest(County: SearchFixtures.Bucuresti, Page: 2), CancellationToken.None);
        Assert.Equal(SearchFixtures.Filler + 1 - CompanySearch.PageSize, browse.Hits.Count);
        var beyond = await search.ExecuteAsync(new SearchRequest(County: SearchFixtures.Bucuresti, Page: 99), CancellationToken.None);
        Assert.Empty(beyond.Hits);
        Assert.Equal(SearchFixtures.Filler + 1, beyond.Total);
    }

    [Fact]
    public async Task RebuildReplacesTheTablesWithTheSameContent()
    {
        await using var database = await SeededAsync();
        var before = await database.SearchCounts.AsNoTracking().OrderBy(item => item.CountyNumber).ThenBy(item => item.Lifecycle).ThenBy(item => item.Caen)
            .Select(item => new { item.CountyNumber, item.Lifecycle, item.Caen, item.Companies }).ToListAsync();
        var build = await new BuildSearchIndex(database, TimeProvider.System).ExecuteAsync(CancellationToken.None);
        var after = await database.SearchCounts.AsNoTracking().OrderBy(item => item.CountyNumber).ThenBy(item => item.Lifecycle).ThenBy(item => item.Caen)
            .Select(item => new { item.CountyNumber, item.Lifecycle, item.Caen, item.Companies }).ToListAsync();
        Assert.Equal(before, after);
        Assert.Equal(2, build.Id);
        Assert.Equal(SearchFixtures.Filler + 6, await database.SearchCompanies.CountAsync());
        Assert.Equal(1, await database.SearchNames.CountAsync(item => !item.Current));
    }

    private async Task<Modules.Platform.Persistence.AppDbContext> SeededAsync()
    {
        var database = await postgres.CreateIsolatedContextAsync();
        await SearchFixtures.SeedAsync(database);
        return database;
    }
}
