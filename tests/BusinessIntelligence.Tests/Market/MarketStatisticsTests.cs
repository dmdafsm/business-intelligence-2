using BusinessIntelligence.Modules.Market;
using BusinessIntelligence.Modules.Registry.Onrc;
using BusinessIntelligence.Tests.Infrastructure;
using BusinessIntelligence.Tests.Search;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BusinessIntelligence.Tests.Market;

public sealed class MarketStatisticsTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task StatisticsCountEveryRegistryEntityAndOnlyObservedStateChanges()
    {
        await using var database = await postgres.CreateIsolatedContextAsync();
        await SearchFixtures.SeedAsync(database);
        // A second build replaces the first rather than adding to it.
        await new BuildMarketStatistics(database, TimeProvider.System).ExecuteAsync(CancellationToken.None);
        var statistics = new MarketStatistics(database);

        var all = (await statistics.ExecuteAsync(new MarketFilter(null, null), CancellationToken.None))!;
        var registered = await database.Companies.CountAsync(item => item.FirstRegistrationDate != null);
        Assert.Equal(registered, all.Years.Sum(item => item.Registered));
        Assert.Equal(registered, all.Total);
        var active = await database.Companies.CountAsync(item => item.Lifecycle == Lifecycle.Active);
        Assert.Equal(new YearRegistrations(2010, registered, active), all.Years.Single());
        Assert.Equal(new DateOnly(2026, 10, 19), all.Build.ReferenceDate);

        // 24 complete months and the snapshot's own month, marked partial.
        Assert.Equal(MarketStatistics.Months + 1, all.Months.Count);
        Assert.Equal((new DateOnly(2026, 10, 1), true), (all.Months[^1].Month, all.Months[^1].Partial));
        Assert.Single(all.Months, item => item.Partial);

        // The insolvency is seen between the two snapshots, not dated.
        var change = Assert.Single(all.Changes);
        Assert.Equal((new DateOnly(2024, 12, 18), new DateOnly(2025, 12, 8)), (change.PreviousSnapshot, change.ObservedOn));
        Assert.Equal(1, change.ByLifecycle[Lifecycle.Insolvency]);
        Assert.Equal([SearchFixtures.Cluj, SearchFixtures.Bucuresti], all.Counties.Select(item => item.County).Order());

        var clujCompanies = (await statistics.ExecuteAsync(new MarketFilter(SearchFixtures.Cluj, "company"), CancellationToken.None))!;
        Assert.Equal(await database.Companies.CountAsync(item => item.CountyNumber == SearchFixtures.Cluj && item.FormCategory == "company"),
            clujCompanies.Years.Single().Registered);
        Assert.Empty(clujCompanies.Counties);
        Assert.Equal(await database.Companies.CountAsync(item => item.CountyNumber == SearchFixtures.Cluj && item.FormCategory == "company" && item.Lifecycle == Lifecycle.StruckOff),
            clujCompanies.States.Single(item => item.Lifecycle == Lifecycle.StruckOff).Companies);

        var none = (await statistics.ExecuteAsync(new MarketFilter(SearchFixtures.Bucuresti, "individual"), CancellationToken.None))!;
        Assert.Empty(none.Years);
        Assert.Empty(none.Changes);
        Assert.All(none.Months, item => Assert.Equal(0, item.Registered));
    }

    [Fact]
    public async Task WithoutABuildThereIsNoReport()
    {
        await using var database = await postgres.CreateIsolatedContextAsync();
        Assert.Null(await new MarketStatistics(database).ExecuteAsync(new MarketFilter(null, null), CancellationToken.None));
    }
}
