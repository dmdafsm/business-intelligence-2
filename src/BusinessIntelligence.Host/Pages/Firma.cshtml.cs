using System.Globalization;
using BusinessIntelligence.Host.Components;
using BusinessIntelligence.Modules.Financials;
using BusinessIntelligence.Modules.Profile;
using BusinessIntelligence.Modules.Search;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BusinessIntelligence.Host.Pages;

public sealed class FirmaModel(CompanyProfileQuery query) : PageModel
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

    /// <summary>Net result as published: profit positive, loss negative; null when the statement gives neither.</summary>
    public static decimal? NetResult(FinancialStatement statement) =>
        statement.NetProfit is > 0 ? statement.NetProfit
        : statement.NetLoss is > 0 ? -statement.NetLoss
        : statement.NetProfit ?? statement.NetLoss;

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
