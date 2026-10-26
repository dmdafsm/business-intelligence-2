using System.Globalization;
using BusinessIntelligence.Host.Components;
using BusinessIntelligence.Modules.Market;
using BusinessIntelligence.Modules.Platform.Persistence;
using BusinessIntelligence.Modules.Registry.Onrc;
using BusinessIntelligence.Modules.Search;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace BusinessIntelligence.Host.Pages;

public sealed class StatisticsModel(MarketStatistics statistics, AppDbContext database) : PageModel
{
    public static readonly CultureInfo Ro = CultureInfo.GetCultureInfo("ro-RO");

    /// <summary>Kinds of registry entities, by URL slug.</summary>
    public static readonly IReadOnlyList<(string Slug, string Category, string Label)> Forms =
    [
        ("societati", "company", "Societăți"),
        ("pfa", "individual", "PFA, II și IF"),
        ("cooperative", "cooperative", "Cooperative"),
        ("altele", "other", "Alte forme"),
    ];

    /// <summary>The states shown as columns of the change table, in the order a company moves through them.</summary>
    public static readonly IReadOnlyList<string> ChangeStates =
        [Lifecycle.Suspended, Lifecycle.Dissolution, Lifecycle.Insolvency, Lifecycle.StruckOff, Lifecycle.Active];

    [BindProperty(SupportsGet = true, Name = "judet")]
    public short? County { get; set; }

    [BindProperty(SupportsGet = true, Name = "forma")]
    public string? Form { get; set; }

    public MarketReport? Report { get; private set; }

    public IReadOnlyDictionary<short, string> Counties { get; private set; } = new Dictionary<short, string>();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var category = Forms.FirstOrDefault(item => item.Slug == Form).Category;
        Form = category is null ? null : Form;
        Counties = (await database.Siruta.AsNoTracking().Where(item => item.Level == 1).ToListAsync(cancellationToken))
            .OrderBy(item => item.CountyNumber).ToDictionary(item => item.CountyNumber, item => PlaceNames.Display(item.Name, true));
        if (County is { } county && county != 0 && !Counties.ContainsKey(county))
        {
            County = null;
        }

        Report = await statistics.ExecuteAsync(new MarketFilter(County, category), cancellationToken);
    }

    public string CountyName(short county) => county == 0 ? "Județ necunoscut" : Counties.GetValueOrDefault(county) ?? county.ToString(CultureInfo.InvariantCulture);

    public string? FormLabel => Forms.FirstOrDefault(item => item.Slug == Form).Label;

    public static string LifecycleLabel(string value) => CompanySearch.LifecycleLabels.FirstOrDefault(item => item.Value == value).Label ?? value;

    public static string Number(int value) => value.ToString("N0", Ro);

    public static string Date(DateOnly date) => date.ToString("dd.MM.yyyy", Ro);

    public static string MonthName(DateOnly month) => month.ToString("MMMM yyyy", Ro);

    /// <summary>Share of a year's registrations still active, with one decimal; empty when nothing was registered.</summary>
    public static string Share(int part, int whole) => whole == 0 ? "—" : ((decimal)part * 100 / whole).ToString("N1", Ro) + "%";

    public IReadOnlyList<BarChart> Charts()
    {
        var report = Report!;
        var complete = report.Months.Where(item => !item.Partial).ToList();
        var months = new BarChart("grafic-luni", "Înregistrări pe lună",
            $"Coloane pe luni pentru înregistrările noi, {MonthName(complete[0].Month)}–{MonthName(complete[^1].Month)}. Toate valorile sunt în tabelul de mai jos.",
            [.. complete.Select((item, index) => new ChartPoint((short)item.Month.Year, item.Registered, $"{MonthName(item.Month)}: {Number(item.Registered)}",
                index % 3 == 0 ? item.Month.ToString("MMM", Ro).TrimEnd('.') : string.Empty))]);
        var years = report.Years.Where(item => item.Year < report.Build.ReferenceDate.Year).ToList();
        var yearly = new BarChart("grafic-ani", "Înregistrări pe an",
            years.Count == 0 ? "Nicio înregistrare." : $"Coloane pe ani pentru înregistrările noi, {years[0].Year}–{years[^1].Year}. Toate valorile sunt în tabelul de mai jos.",
            [.. years.Select(item => new ChartPoint((short)item.Year, item.Registered, $"{item.Year}: {Number(item.Registered)}",
                item.Year % 5 == 0 ? item.Year.ToString(CultureInfo.InvariantCulture) : string.Empty))]);
        return [months, yearly];
    }

    /// <summary>The query string for the current filters with one of them changed.</summary>
    public string Link(short? county, string? form)
    {
        var parts = new List<string>();
        if (county is { } value) { parts.Add("judet=" + value.ToString(CultureInfo.InvariantCulture)); }
        if (form is not null) { parts.Add("forma=" + form); }
        return Url.Page("/Statistics") + (parts.Count == 0 ? string.Empty : "?" + string.Join("&", parts));
    }
}
