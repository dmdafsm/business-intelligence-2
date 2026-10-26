namespace BusinessIntelligence.Modules.Financials;

public enum IndicatorUnit
{
    Ratio,
    Lei,
}

/// <summary>A derived indicator: its key in <c>financials.benchmarks</c>, the name and formula shown on pages, and its unit.</summary>
public sealed record IndicatorDefinition(string Key, string Name, string Formula, IndicatorUnit Unit);

/// <summary>A derived value, or the reason it cannot be calculated. Exactly one of the two is set.</summary>
public sealed record IndicatorValue(decimal? Value, string? Reason)
{
    public static IndicatorValue Of(decimal value) => new(value, null);

    public static IndicatorValue Missing(string reason) => new(null, reason);
}

/// <summary>
/// The derived indicators of <c>indicators-v1</c>, calculated from published statements only: a blank component is never
/// read as zero. <see cref="BuildFinancialBenchmarks"/> applies the same formulas in SQL; a test keeps the two in step.
/// </summary>
public static class FinancialIndicators
{
    public const string Version = "indicators-v1";

    public static readonly IndicatorDefinition TurnoverGrowth = new("turnover_growth", "Creșterea cifrei de afaceri",
        "(cifra de afaceri − cifra de afaceri a anului anterior) / cifra de afaceri a anului anterior", IndicatorUnit.Ratio);

    public static readonly IndicatorDefinition NetMargin = new("net_margin", "Marja netă",
        "rezultat net / cifra de afaceri", IndicatorUnit.Ratio);

    public static readonly IndicatorDefinition TurnoverPerEmployee = new("turnover_per_employee", "Cifra de afaceri pe salariat",
        "cifra de afaceri / număr mediu de salariați", IndicatorUnit.Lei);

    public static readonly IndicatorDefinition ReturnOnEquity = new("return_on_equity", "Rentabilitatea capitalurilor proprii",
        "rezultat net / capitaluri proprii", IndicatorUnit.Ratio);

    public static readonly IndicatorDefinition DebtRatio = new("debt_ratio", "Gradul de îndatorare",
        "datorii / (active imobilizate + active circulante)", IndicatorUnit.Ratio);

    public static readonly IReadOnlyList<IndicatorDefinition> All = [TurnoverGrowth, NetMargin, TurnoverPerEmployee, ReturnOnEquity, DebtRatio];

    /// <summary>Size bands by average employees; a statement with blank employees is only in the all-sizes cohort.</summary>
    public const string AllSizes = "all";

    public static readonly IReadOnlyList<(string Key, string Label)> SizeBands =
        [("0", "fără salariați"), ("1-9", "1–9 salariați"), ("10-49", "10–49 salariați"), ("50-249", "50–249 salariați"), ("250+", "250+ salariați")];

    public static string? SizeBand(int? employees) => employees switch
    {
        null => null,
        0 => "0",
        < 10 => "1-9",
        < 50 => "10-49",
        < 250 => "50-249",
        _ => "250+",
    };

    /// <summary>Net result as published: profit positive, loss negative; null when the statement gives neither.</summary>
    public static decimal? NetResult(FinancialStatement statement) =>
        statement.NetProfit is > 0 ? statement.NetProfit
        : statement.NetLoss is > 0 ? -statement.NetLoss
        : statement.NetProfit ?? statement.NetLoss;

    /// <param name="previous">The statement of the year before, or null when none was filed.</param>
    public static IndicatorValue Calculate(IndicatorDefinition indicator, FinancialStatement statement, FinancialStatement? previous)
    {
        var turnover = statement.Turnover;
        var net = NetResult(statement);
        return indicator.Key switch
        {
            "turnover_growth" => previous is null ? IndicatorValue.Missing("nicio situație depusă pentru anul anterior")
                : previous.Turnover is not { } before || turnover is null ? IndicatorValue.Missing("cifra de afaceri necompletată")
                : before <= 0 ? IndicatorValue.Missing("cifra de afaceri a anului anterior este zero")
                : IndicatorValue.Of((turnover.Value - before) / before),
            "net_margin" => net is null ? IndicatorValue.Missing("rezultatul net necompletat")
                : turnover is null ? IndicatorValue.Missing("cifra de afaceri necompletată")
                : turnover <= 0 ? IndicatorValue.Missing("fără cifră de afaceri")
                : IndicatorValue.Of(net.Value / turnover.Value),
            "turnover_per_employee" => turnover is null ? IndicatorValue.Missing("cifra de afaceri necompletată")
                : statement.Employees is not { } employees ? IndicatorValue.Missing("numărul de salariați necompletat")
                : employees <= 0 ? IndicatorValue.Missing("fără salariați")
                : IndicatorValue.Of(turnover.Value / employees),
            "return_on_equity" => net is null ? IndicatorValue.Missing("rezultatul net necompletat")
                : statement.Equity is not { } equity ? IndicatorValue.Missing("capitalurile proprii necompletate")
                : equity <= 0 ? IndicatorValue.Missing("capitaluri proprii negative sau zero")
                : IndicatorValue.Of(net.Value / equity),
            "debt_ratio" => statement.Liabilities is not { } liabilities ? IndicatorValue.Missing("datoriile necompletate")
                : statement.FixedAssets is null || statement.CurrentAssets is null ? IndicatorValue.Missing("activele necompletate")
                : statement.FixedAssets + statement.CurrentAssets is not > 0 ? IndicatorValue.Missing("fără active")
                : IndicatorValue.Of(liabilities / (statement.FixedAssets.Value + statement.CurrentAssets.Value)),
            _ => throw new ArgumentOutOfRangeException(nameof(indicator), indicator.Key, "Unknown indicator."),
        };
    }
}
