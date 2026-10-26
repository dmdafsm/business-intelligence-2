using BusinessIntelligence.Modules.Profile;
using BusinessIntelligence.Modules.Search;
using BusinessIntelligence.Tests.Infrastructure;
using BusinessIntelligence.Tests.Search;
using Xunit;

namespace BusinessIntelligence.Tests.Profile;

public sealed class CompanyProfileTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task ProfileGathersRegistryFiscalAndFinancialHistoryByCui()
    {
        await using var database = await postgres.CreateIsolatedContextAsync();
        await SearchFixtures.SeedAsync(database);
        var profile = await new CompanyProfileQuery(database).ExecuteAsync(SearchFixtures.ExempluSoft, null, CancellationToken.None);

        Assert.NotNull(profile);
        Assert.Equal("J2024000100123", profile.CurrentRegistration!.RegistrationNumber);
        Assert.Equal(["J2024000100123", "J08/5/2010"], profile.Registrations.Select(item => item.RegistrationNumber));
        Assert.Equal([("6210", "Activități de programare la comandă"), ("6220", "Consultanță în tehnologia informației")],
            profile.Activities.Select(item => (item.Code, item.Name)));

        // History by registry number and by CUI: the earlier name and the number only history knows.
        Assert.Contains(profile.RegistryHistory, period => period.Name == "VECHI NUME S.R.L.");
        Assert.Contains(profile.RegistryHistory, period => period.RegistrationNumber == "J12/99/2000");
        Assert.Equal("0264000002", profile.LatestFiscal!.Phone);
        Assert.Equal(2, profile.FiscalHistory.Count);
        Assert.Equal("Cluj", profile.Counties[SearchFixtures.Cluj]);

        // Only name, role, birth year and a firm's place reach the page (ADR 0001); the birth date and place stay stored.
        Assert.Equal(
            [new PersonEntry("J2024000100123", "POPESCU ANA", "administrator", "natural", 1980, null),
             new PersonEntry("J2024000100123", "EXEMPLU INSOLV IPURL", "lichidator judiciar", "legal", null, "Cluj-Napoca, Cluj, România")],
            profile.People);

        // Every year of the range; years without a statement stay empty, blank cells stay null, zero stays zero.
        Assert.Equal([2010, 2011, 2012, 2013, 2014], profile.Financials.Select(year => (int)year.Year));
        Assert.Null(profile.Financials[1].Statement);
        Assert.Null(profile.Financials[4].Statement);
        Assert.Null(profile.Financials[3].Statement!.Turnover);
        Assert.Equal(0, profile.Financials[2].Statement!.Employees);
        Assert.Equal("web2012.txt", profile.Financials[2].Source!.FileName);
        Assert.Equal("Ministerul Finanțelor", profile.FiscalSource!.Publisher);
    }

    [Fact]
    public async Task CompaniesWithoutCuiAreFoundByRegistryNumberOnly()
    {
        await using var database = await postgres.CreateIsolatedContextAsync();
        await SearchFixtures.SeedAsync(database);
        var query = new CompanyProfileQuery(database);

        var individual = await query.ExecuteAsync(null, "F12/5/2010", CancellationToken.None);
        Assert.Equal("POPESCU ION PFA", individual!.Company.Name);
        Assert.Empty(individual.FiscalHistory);
        Assert.All(individual.Financials, year => Assert.Null(year.Statement));

        // A company that has a CUI is addressed by it, not by registry number; unknown identifiers find nothing.
        Assert.Null(await query.ExecuteAsync(null, "J2024000100123", CancellationToken.None));
        Assert.Null(await query.ExecuteAsync(SearchFixtures.Cui(123456), null, CancellationToken.None));
        Assert.Null(await query.ExecuteAsync(null, null, CancellationToken.None));
    }

    [Fact]
    public async Task EarlierRegistryNumbersFromHistoryFindTheCompany()
    {
        await using var database = await postgres.CreateIsolatedContextAsync();
        await SearchFixtures.SeedAsync(database);
        var result = await new CompanySearch(database).ExecuteAsync(new SearchRequest("J12/0099/2000"), CancellationToken.None);
        Assert.Equal(SearchFixtures.ExempluSoft, Assert.Single(result.Hits).Cui);
    }
}
