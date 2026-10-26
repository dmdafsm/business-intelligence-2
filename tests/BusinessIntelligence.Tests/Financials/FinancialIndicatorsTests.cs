using BusinessIntelligence.Modules.Financials;
using BusinessIntelligence.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BusinessIntelligence.Tests.Financials;

public sealed class FinancialIndicatorsTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static FinancialStatement Statement(
        long cui = 1, short year = 2024, decimal? turnover = 1_000, decimal? netProfit = 100, decimal? netLoss = 0, int? employees = 5,
        decimal? equity = 500, decimal? liabilities = 300, decimal? fixedAssets = 400, decimal? currentAssets = 200, string caen = "4711", short? version = 2) => new()
        {
            Cui = cui,
            Year = year,
            Family = "UU",
            CaenCode = caen,
            CaenVersion = version,
            SourceFileId = 1,
            Turnover = turnover,
            NetProfit = netProfit,
            NetLoss = netLoss,
            Employees = employees,
            Equity = equity,
            Liabilities = liabilities,
            FixedAssets = fixedAssets,
            CurrentAssets = currentAssets,
        };

    [Fact]
    public void IndicatorsUsePublishedComponentsAndALossCountsAsNegative()
    {
        var current = Statement(turnover: 1_200, netProfit: 0, netLoss: 60);
        var previous = Statement(year: 2023, turnover: 1_000);

        Assert.Equal(0.2m, FinancialIndicators.Calculate(FinancialIndicators.TurnoverGrowth, current, previous).Value);
        Assert.Equal(-0.05m, FinancialIndicators.Calculate(FinancialIndicators.NetMargin, current, previous).Value);
        Assert.Equal(240m, FinancialIndicators.Calculate(FinancialIndicators.TurnoverPerEmployee, current, previous).Value);
        Assert.Equal(-0.12m, FinancialIndicators.Calculate(FinancialIndicators.ReturnOnEquity, current, previous).Value);
        Assert.Equal(0.5m, FinancialIndicators.Calculate(FinancialIndicators.DebtRatio, current, previous).Value);
    }

    [Theory]
    [InlineData("turnover_growth", "nicio situație depusă pentru anul anterior")]
    [InlineData("net_margin", "fără cifră de afaceri")]
    [InlineData("turnover_per_employee", "fără salariați")]
    [InlineData("return_on_equity", "capitaluri proprii negative sau zero")]
    [InlineData("debt_ratio", "activele necompletate")]
    public void ZeroAndBlankComponentsGiveAReasonInsteadOfAValue(string key, string reason)
    {
        // Zero turnover, zero employees, negative equity and a blank current-assets cell: none is read as a number.
        var statement = Statement(turnover: 0, employees: 0, equity: -10, currentAssets: null);
        var value = FinancialIndicators.Calculate(FinancialIndicators.All.Single(item => item.Key == key), statement, previous: null);

        Assert.Null(value.Value);
        Assert.Equal(reason, value.Reason);
    }

    [Fact]
    public void BlankResultIsNotZero()
    {
        var statement = Statement(netProfit: null, netLoss: null);
        Assert.Equal("rezultatul net necompletat", FinancialIndicators.Calculate(FinancialIndicators.NetMargin, statement, null).Reason);
        Assert.Equal(0m, FinancialIndicators.Calculate(FinancialIndicators.NetMargin, Statement(netProfit: 0, netLoss: 0), null).Value);
    }

    [Fact]
    public async Task BenchmarksMatchTheProfileFormulasAndHideSmallCohorts()
    {
        await using var database = await postgres.CreateIsolatedContextAsync();
        // Twelve 1–9 employee companies in 4711 (2024, with 2023 for growth), three in 50–249, one with blank employees,
        // and a 2025 statement whose CAEN version is unknown.
        var current = Enumerable.Range(1, 12).Select(index => Statement(cui: index, turnover: 1_000 + 137 * index, netProfit: index % 4 == 0 ? 0 : 11 * index,
            netLoss: index % 4 == 0 ? 7 * index : 0, employees: 1 + index % 9, equity: index == 3 ? -5 : 100 + 13 * index, liabilities: 50 * index))
            .Concat(Enumerable.Range(20, 3).Select(index => Statement(cui: index, employees: 60)))
            .Append(Statement(cui: 30, employees: null))
            .ToList();
        var previous = Enumerable.Range(1, 12).Select(index => Statement(cui: index, year: 2023, turnover: index == 5 ? 0 : 900 + 50 * index)).ToList();
        database.FinancialStatements.AddRange([.. current, .. previous, Statement(cui: 1, year: 2025, version: null)]);
        await database.SaveChangesAsync();

        var builder = new BuildFinancialBenchmarks(database, TimeProvider.System);
        await builder.ExecuteAsync(CancellationToken.None);
        var rows = (await builder.ExecuteAsync(CancellationToken.None)).Rows;
        var stored = await database.FinancialBenchmarks.AsNoTracking().ToListAsync();

        // Rebuilding replaces the table: 2023 and 2024, each with all sizes plus the bands present; never 2025.
        Assert.Equal(stored.Count, rows);
        Assert.DoesNotContain(stored, item => item.Year == 2025);
        Assert.Equal(["1-9", "50-249", FinancialIndicators.AllSizes], stored.Where(item => item.Year == 2024).Select(item => item.SizeBand).Distinct().Order());

        var small = stored.Single(item => item is { Year: 2024, SizeBand: "1-9", Indicator: "net_margin" });
        Assert.Equal(12, small.Population);
        foreach (var indicator in FinancialIndicators.All)
        {
            var expected = current.Where(item => item.Employees is > 0 and < 10)
                .Select(item => FinancialIndicators.Calculate(indicator, item, previous.SingleOrDefault(before => before.Cui == item.Cui)).Value)
                .OfType<decimal>().Order().ToList();
            var cohort = stored.Single(item => item.Year == 2024 && item.SizeBand == "1-9" && item.Indicator == indicator.Key);
            Assert.Equal(expected.Count, cohort.Population);
            Assert.Equal(12, cohort.Statements);
            if (expected.Count >= BuildFinancialBenchmarks.MinimumPopulation)
            {
                Assert.Equal(Math.Round(Median(expected), 6), cohort.P50);
            }
            else
            {
                Assert.Null(cohort.P50);
            }
        }

        // Three companies are too few for percentiles; the count is still kept.
        var medium = stored.Single(item => item is { Year: 2024, SizeBand: "50-249", Indicator: "net_margin" });
        Assert.Equal(3, medium.Population);
        Assert.Null(medium.P10);
        Assert.Equal(16, stored.Single(item => item is { Year: 2024, SizeBand: "all", Indicator: "net_margin" }).Statements);
    }

    private static decimal Median(List<decimal> sorted) =>
        sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[(sorted.Count / 2) - 1] + sorted[sorted.Count / 2]) / 2;
}
