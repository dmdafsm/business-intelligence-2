using BusinessIntelligence.Host.Components;
using Xunit;

namespace BusinessIntelligence.BrowserTests;

public sealed class BarChartTests
{
    [Theory]
    [InlineData(13_157_835_535, "13,2 mld")]
    [InlineData(1_635_337_205, "1,6 mld")]
    [InlineData(-64_331, "-64,3 mii")]
    [InlineData(12_702, "12,7 mii")]
    [InlineData(5_000, "5000")]
    [InlineData(0, "0")]
    public void ValuesAreShortenedInRomanian(decimal value, string expected) => Assert.Equal(expected, BarChart.Compact(value));

    [Fact]
    public void LossesGoBelowTheBaselineAndGapsStayGaps()
    {
        var chart = new BarChart("test", "Rezultat", "test", [
            new ChartPoint(2010, 120_000, "2010"), new ChartPoint(2011, null, "2011: nedepus"), new ChartPoint(2012, -45_000, "2012"), new ChartPoint(2013, 0, "2013")]);

        Assert.True(chart.Min < 0 && chart.Max >= 120_000);
        Assert.True(chart.Columns[0].Y < chart.Baseline);
        Assert.True(chart.Columns[2].Y > chart.Baseline);
        Assert.Equal(chart.Baseline, chart.Columns[1].Y);
        Assert.Empty(chart.ColumnPath(chart.Columns[1]));
        Assert.Empty(chart.ColumnPath(chart.Columns[3]));
        Assert.StartsWith("M", chart.ColumnPath(chart.Columns[2]), StringComparison.Ordinal);
        Assert.Equal(2013, chart.Latest!.Point.Year);
    }
}
