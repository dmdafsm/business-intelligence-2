using System.Globalization;
using BusinessIntelligence.Host.Components;
using BusinessIntelligence.Modules.Financials;
using BusinessIntelligence.Modules.Profile;
using BusinessIntelligence.Modules.Search;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BusinessIntelligence.Host.Pages;

public sealed class CompanyModel(CompanyProfileQuery query) : PageModel
{
    public static readonly CultureInfo Ro = CultureInfo.GetCultureInfo("ro-RO");

    /// <summary>The published indicators, in statement order: assets, liabilities and equity, then the profit and loss account.</summary>
    public static readonly IReadOnlyList<(string Label, Func<FinancialStatement, decimal?> Value)> Indicators =
    [
        ("Active imobilizate", item => item.FixedAssets),
        ("Active circulante", item => item.CurrentAssets),
        ("Stocuri", item => item.Inventories),
        ("Creanțe", item => item.Receivables),
        ("Casa și conturi la bănci", item => item.Cash),
        ("Cheltuieli în avans", item => item.PrepaidExpenses),
        ("Datorii", item => item.Liabilities),
        ("Venituri în avans", item => item.DeferredIncome),
        ("Provizioane", item => item.Provisions),
        ("Capitaluri proprii", item => item.Equity),
        ("Capital subscris vărsat", item => item.ShareCapital),
        ("Patrimoniul regiei", item => item.RegiePatrimony),
        ("Patrimoniul public", item => item.PublicPatrimony),
        ("Cifra de afaceri netă", item => item.Turnover),
        ("Venituri totale", item => item.TotalIncome),
        ("Cheltuieli totale", item => item.TotalExpenses),
        ("Profit brut", item => item.GrossProfit),
        ("Pierdere brută", item => item.GrossLoss),
        ("Profit net", item => item.NetProfit),
        ("Pierdere netă", item => item.NetLoss),
    ];

    /// <summary>ANAF obligation codes named in the fiscal import guide; other codes are shown as published.</summary>
    public static readonly IReadOnlyDictionary<int, string> Obligations = new Dictionary<int, string>
    {
        [100] = "impozit pe profit",
        [120] = "impozit pe veniturile microîntreprinderilor",
        [602] = "impozit pe salarii",
    };

    [BindProperty(SupportsGet = true)]
    public long? Cui { get; set; }

    [BindProperty(SupportsGet = true, Name = "nr")]
    public string? RegistrationNumber { get; set; }

    public CompanyProfile Profile { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var profile = await query.ExecuteAsync(Cui, Cui is null ? RegistrationNumber : null, cancellationToken);
        if (profile is null)
        {
            return NotFound();
        }

        Profile = profile;
        return Page();
    }

    public bool Individual => Profile.Company.FormCategory == "individual";

    /// <summary>ANAF administers large and medium taxpayers through dedicated offices; the office name says which.</summary>
    public static string? TaxpayerCategory(string? office) =>
        office is null ? null
        : office.Contains("Marilor Contribuabili", StringComparison.OrdinalIgnoreCase) || office.Contains("Mari Contribuabili", StringComparison.OrdinalIgnoreCase) ? "mare contribuabil"
        : office.Contains("Contribuabili Mijlocii", StringComparison.OrdinalIgnoreCase) ? "contribuabil mijlociu"
        : null;

    public static string YesNo(bool? value, DateOnly? since = null) => value switch
    {
        true => since is { } date ? $"Da, din {Date(date)}" : "Da",
        false => "Nu",
        null => "nedeclarat",
    };

    public string Lifecycle(string value) =>
        CompanySearch.LifecycleLabels.FirstOrDefault(item => item.Value == value).Label ?? value;

    public string? Place(int? code) => code is { } value ? Profile.Places.GetValueOrDefault(value) : null;

    public string? County(short? number) => number is { } value ? Profile.Counties.GetValueOrDefault(value) : null;

    public static string Date(DateOnly? date) => date?.ToString("dd.MM.yyyy", Ro) ?? string.Empty;

    public static string Amount(decimal value) => value.ToString("N0", Ro);

    public static decimal? NetResult(FinancialStatement statement) => FinancialIndicators.NetResult(statement);

    /// <summary>Ratios as percentages with one decimal, lei amounts whole.</summary>
    public static string Indicator(IndicatorDefinition indicator, decimal value) => indicator.Unit == IndicatorUnit.Ratio
        ? (value * 100).ToString("N1", Ro) + "%"
        : Amount(Math.Round(value, MidpointRounding.AwayFromZero)) + " lei";

    /// <summary>Where the company's value falls in the cohort's distribution; null when the cohort has no percentiles.</summary>
    public static string? Position(decimal value, FinancialBenchmark cohort) => cohort switch
    {
        { P10: null } => null,
        _ when value < cohort.P10 => "sub 90% din firme",
        _ when value < cohort.P25 => "în sfertul de jos",
        _ when value < cohort.P50 => "sub mediană",
        _ when value == cohort.P50 => "la mediană",
        _ when value <= cohort.P75 => "peste mediană",
        _ when value <= cohort.P90 => "în sfertul de sus",
        _ => "peste 90% din firme",
    };

    /// <summary>What a signal refers to, in one sentence ending with a full stop.</summary>
    public static string SignalDetail(BusinessIntelligence.Modules.Risk.RiskSignal signal) => signal.Signal switch
    {
        BusinessIntelligence.Modules.Risk.RiskSignals.AnafInactive =>
            (signal.Since is { } since ? $"Inactiv din {Date(since)}" : "Inactiv") + (signal.CheckedOn is { } checkedOn ? $", conform răspunsului ANAF din {Date(checkedOn)}." : "."),
        BusinessIntelligence.Modules.Risk.RiskSignals.NoRecentStatement => $"Nicio situație financiară publicată pentru {signal.Year - 1} și {signal.Year}.",
        BusinessIntelligence.Modules.Risk.RiskSignals.NegativeEquity => $"{Amount(signal.Amount ?? 0)} lei în {signal.Year}.",
        BusinessIntelligence.Modules.Risk.RiskSignals.RepeatedLosses =>
            $"Pierdere netă în {signal.Year - 2}, {signal.Year - 1} și {signal.Year}" + (signal.Amount is { } loss ? $"; în {signal.Year}: {Amount(loss)} lei." : "."),
        BusinessIntelligence.Modules.Risk.RiskSignals.HighDebtRatio =>
            $"{Indicator(FinancialIndicators.DebtRatio, signal.Ratio ?? 0)} în {signal.Year}, peste {Indicator(FinancialIndicators.DebtRatio, signal.Threshold ?? 0)} (percentila 90 a clasei CAEN {signal.CaenCode}).",
        _ => string.Empty,
    };

    public static string SizeLabel(string band) =>
        band == FinancialIndicators.AllSizes ? "toate mărimile" : FinancialIndicators.SizeBands.FirstOrDefault(item => item.Key == band).Label ?? band;

    /// <summary>Derived indicators for one filed year, with the year before as the base for growth.</summary>
    public IReadOnlyList<IndicatorValue> Derived(FinancialYear year)
    {
        var previous = Profile.Financials.FirstOrDefault(item => item.Year == year.Year - 1)?.Statement;
        return [.. FinancialIndicators.All.Select(indicator => FinancialIndicators.Calculate(indicator, year.Statement!, previous))];
    }

    public IReadOnlyList<BarChart> Charts()
    {
        var years = Profile.Financials;
        return
        [
            Chart("grafic-cifra", "Cifra de afaceri netă (lei)", "cifra de afaceri", years.Select(year => (year, year.Statement?.Turnover))),
            Chart("grafic-rezultat", "Profit sau pierdere netă (lei)", "rezultatul net; pierderile sub axă", years.Select(year => (year, year.Statement is { } item ? NetResult(item) : null))),
            Chart("grafic-salariati", "Număr mediu de salariați", "numărul mediu de salariați", years.Select(year => (year, (decimal?)year.Statement?.Employees))),
        ];

        static BarChart Chart(string id, string title, string what, IEnumerable<(FinancialYear Year, decimal? Value)> values)
        {
            var points = values.Select(item => new ChartPoint(item.Year.Year, item.Value,
                item.Year.Statement is null ? $"{item.Year.Year}: nedepus"
                : item.Value is { } value ? $"{item.Year.Year}: {Amount(value)}"
                : $"{item.Year.Year}: necompletat")).ToList();
            var filed = points.Where(point => point.Value is not null).ToList();
            var description = filed.Count == 0
                ? $"Nicio valoare pentru {what}."
                : $"Coloane pe ani pentru {what}, {points[0].Year}–{points[^1].Year}. Ultima valoare: {filed[^1].Tooltip}. Toate valorile sunt în tabelul de mai jos.";
            return new BarChart(id, title, description, points);
        }
    }
}
