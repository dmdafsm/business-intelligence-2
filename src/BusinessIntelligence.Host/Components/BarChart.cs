using System.Globalization;

namespace BusinessIntelligence.Host.Components;

/// <summary>One year of a chart; a null value is a year without a statement or a blank cell, drawn as a gap, never as zero.</summary>
public sealed record ChartPoint(short Year, decimal? Value, string Tooltip);

/// <summary>
/// A server-rendered column chart: one series, one axis, columns from a zero baseline (losses below it), recessive
/// gridlines and the latest value labeled. The table next to it holds every value; the chart works without JavaScript.
/// </summary>
public sealed class BarChart
{
    public const double Width = 360;
    public const double Height = 200;
    public const double Left = 52;
    public const double Right = 8;
    public const double Top = 18;
    public const double Bottom = 24;
    public const double MaxColumn = 24;
    public const double Corner = 4;
    private static readonly CultureInfo Ro = CultureInfo.GetCultureInfo("ro-RO");

    public BarChart(string id, string title, string description, IReadOnlyList<ChartPoint> points)
    {
        Id = id;
        Title = title;
        Description = description;
        var values = points.Where(point => point.Value is not null).Select(point => point.Value!.Value).ToList();
        var high = values.Count == 0 ? 1m : Math.Max(0m, values.Max());
        var low = values.Count == 0 ? 0m : Math.Min(0m, values.Min());
        Step = NiceStep((high - low) / 3m);
        Max = Math.Ceiling(high / Step) * Step;
        Min = Math.Floor(low / Step) * Step;
        if (Max == Min)
        {
            Max = Min + Step;
        }

        var slot = (Width - Left - Right) / Math.Max(1, points.Count);
        var column = Math.Min(MaxColumn, slot - 2);
        Columns = [.. points.Select((point, index) =>
        {
            var center = Left + slot * (index + 0.5);
            return new ChartColumn(point, center, column, point.Value is { } value ? Y(value) : Baseline, slot,
                points.Count <= 10 || (points.Count - 1 - index) % 2 == 0);
        })];
        Ticks = [.. Enumerable.Range(0, (int)((Max - Min) / Step) + 1).Select(index => Min + Step * index).Select(value => (Y(value), Compact(value)))];
        Latest = Columns.LastOrDefault(item => item.Point.Value is not null);
        HasData = values.Count > 0;
    }

    public string Id { get; }
    public string Title { get; }
    public string Description { get; }
    public decimal Max { get; }
    public decimal Min { get; }
    public decimal Step { get; }
    public bool HasData { get; }
    public IReadOnlyList<ChartColumn> Columns { get; }
    public IReadOnlyList<(double Y, string Label)> Ticks { get; }
    public ChartColumn? Latest { get; }
    public double Baseline => Y(0);

    public double Y(decimal value) => Top + (double)((Max - value) / (Max - Min)) * (Height - Top - Bottom);

    /// <summary>
    /// SVG path of a column from the baseline to <paramref name="y"/>: square at the baseline, the data end rounded by
    /// <see cref="Corner"/> (or less for a short column).
    /// </summary>
    public string ColumnPath(ChartColumn column)
    {
        var x0 = column.Center - column.Width / 2;
        var x1 = column.Center + column.Width / 2;
        var height = Math.Abs(Baseline - column.Y);
        if (height < 0.5)
        {
            return string.Empty;
        }

        var r = Math.Min(Corner, Math.Min(height, column.Width / 2));
        var direction = column.Y < Baseline ? 1 : -1;
        return string.Create(CultureInfo.InvariantCulture,
            $"M{x0:0.##},{Baseline:0.##}V{column.Y + direction * r:0.##}Q{x0:0.##},{column.Y:0.##} {x0 + r:0.##},{column.Y:0.##}H{x1 - r:0.##}Q{x1:0.##},{column.Y:0.##} {x1:0.##},{column.Y + direction * r:0.##}V{Baseline:0.##}Z");
    }

    public static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>Short Romanian magnitude: 1,2 mld, 350 mil, 12 mii.</summary>
    public static string Compact(decimal value)
    {
        var magnitude = Math.Abs(value);
        var (scaled, suffix) = magnitude >= 1_000_000_000m ? (value / 1_000_000_000m, " mld")
            : magnitude >= 1_000_000m ? (value / 1_000_000m, " mil")
            : magnitude >= 10_000m ? (value / 1_000m, " mii")
            : (value, string.Empty);
        return (Math.Abs(scaled) >= 100 ? Math.Round(scaled) : Math.Round(scaled, 1)).ToString("0.#", Ro) + suffix;
    }

    private static decimal NiceStep(decimal rough)
    {
        if (rough <= 0)
        {
            return 1;
        }

        var exponent = Math.Floor(Math.Log10((double)rough));
        var power = (decimal)Math.Pow(10, exponent);
        var fraction = rough / power;
        var nice = fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 2.5m ? 2.5m : fraction <= 5 ? 5 : 10;
        return Math.Max(1, nice * power);
    }
}

public sealed record ChartColumn(ChartPoint Point, double Center, double Width, double Y, double Slot, bool ShowYear);
