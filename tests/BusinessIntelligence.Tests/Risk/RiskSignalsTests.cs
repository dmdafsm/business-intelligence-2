using BusinessIntelligence.Modules.Anaf;
using BusinessIntelligence.Modules.Financials;
using BusinessIntelligence.Modules.Risk;
using BusinessIntelligence.Modules.Search;
using BusinessIntelligence.Tests.Infrastructure;
using BusinessIntelligence.Tests.Search;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BusinessIntelligence.Tests.Risk;

public sealed class RiskSignalsTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task SignalsFollowThePublishedDataAndMissingDataRaisesNone()
    {
        await using var database = await postgres.CreateIsolatedContextAsync();
        await SearchFixtures.SeedAsync(database, build: false);
        // ALFA EXEMPLU (Bucharest, CAEN 6201): losses 2011–2013, negative equity and a debt ratio of 0.9 in 2013 against a
        // class P90 of 0.8. ZARZAVAT FRESH was inactive at ANAF, then reactivated: only the latest answer counts.
        database.FinancialStatements.AddRange(
            Statement(2011, netLoss: 10, equity: 5),
            Statement(2012, netLoss: 20, equity: 1),
            Statement(2013, netLoss: 30, equity: -40, liabilities: 900, fixedAssets: 500, currentAssets: 500));
        database.FinancialBenchmarks.Add(new FinancialBenchmark
        {
            Year = 2013,
            CaenVersion = 2,
            CaenCode = "6201",
            SizeBand = FinancialIndicators.AllSizes,
            Indicator = FinancialIndicators.DebtRatio.Key,
            Statements = 20,
            Population = 20,
            P10 = 0.1m,
            P25 = 0.2m,
            P50 = 0.4m,
            P75 = 0.6m,
            P90 = 0.8m,
            FormulaVersion = FinancialIndicators.Version,
            BuiltAt = DateTimeOffset.UnixEpoch,
        });
        database.AnafStatuses.AddRange(
            new AnafStatus { Cui = SearchFixtures.Zarzavat, CheckedOn = new DateOnly(2024, 1, 1), Found = true, Inactive = true, InactiveSince = new DateOnly(2023, 5, 1) },
            new AnafStatus { Cui = SearchFixtures.Zarzavat, CheckedOn = new DateOnly(2025, 1, 1), Found = true, Inactive = false },
            new AnafStatus { Cui = SearchFixtures.Exemplu, CheckedOn = new DateOnly(2025, 1, 1), Found = true, Inactive = true });
        await database.SaveChangesAsync();
        var builder = new BuildRiskSignals(database, TimeProvider.System);
        await builder.ExecuteAsync(CancellationToken.None);
        var build = await builder.ExecuteAsync(CancellationToken.None);
        // The search index carries the signals, so it is built after them.
        await new BuildSearchIndex(database, TimeProvider.System).ExecuteAsync(CancellationToken.None);
        var signals = await database.RiskSignals.AsNoTracking().ToListAsync();

        Assert.Equal(((short?)2013, (short?)2013), (build.FinancialYear, build.BenchmarkYear));
        var alfa = signals.Where(item => item.Cui == SearchFixtures.AlfaExemplu).ToDictionary(item => item.Signal);
        Assert.Equal([RiskSignals.HighDebtRatio, RiskSignals.NegativeEquity, RiskSignals.RepeatedLosses], alfa.Keys.Order());
        Assert.Equal(-40, alfa[RiskSignals.NegativeEquity].Amount);
        Assert.Equal(-30, alfa[RiskSignals.RepeatedLosses].Amount);
        Assert.Equal((0.9m, 0.8m, "6201"), (alfa[RiskSignals.HighDebtRatio].Ratio!.Value, alfa[RiskSignals.HighDebtRatio].Threshold!.Value, alfa[RiskSignals.HighDebtRatio].CaenCode));

        // EXEMPLU SOFT filed 2013 with a profit and positive equity, and ANAF says active: nothing.
        Assert.DoesNotContain(signals, item => item.Cui == SearchFixtures.ExempluSoft);
        // Reactivated at ANAF; in insolvency, so not expected to file: nothing.
        Assert.DoesNotContain(signals, item => item.Cui == SearchFixtures.Zarzavat);
        // Struck off: no signals, even with an inactive ANAF answer.
        Assert.DoesNotContain(signals, item => item.Cui == SearchFixtures.Exemplu);
        // An active company registered in 2010 with no 2012 or 2013 statement; the PFA has no CUI and is never flagged.
        var filler = Assert.Single(signals, item => item.Cui == SearchFixtures.Cui(60001));
        Assert.Equal((RiskSignals.NoRecentStatement, (short?)2013), (filler.Signal, filler.Year));
        Assert.All(signals, item => Assert.NotNull(item.Cui));

        var search = new CompanySearch(database);
        var losses = await search.ExecuteAsync(new SearchRequest(Signal: RiskSignals.RepeatedLosses), CancellationToken.None);
        Assert.Equal(SearchFixtures.AlfaExemplu, Assert.Single(losses.Hits).Cui);
        var none = await search.ExecuteAsync(new SearchRequest(County: SearchFixtures.Cluj, Signal: CompanySearch.NoSignal), CancellationToken.None);
        Assert.Equal(new[] { SearchFixtures.ExempluSoft, SearchFixtures.Zarzavat }.Order(), none.Hits.Select(item => item.Cui!.Value).Order());
    }

    private static FinancialStatement Statement(short year, decimal netLoss, decimal equity, decimal? liabilities = null, decimal? fixedAssets = null,
        decimal? currentAssets = null) => new()
        {
            Year = year,
            Cui = SearchFixtures.AlfaExemplu,
            Family = "BL",
            CaenCode = "6201",
            CaenVersion = 2,
            SourceFileId = 101,
            NetProfit = 0,
            NetLoss = netLoss,
            Equity = equity,
            Liabilities = liabilities,
            FixedAssets = fixedAssets,
            CurrentAssets = currentAssets,
        };
}
