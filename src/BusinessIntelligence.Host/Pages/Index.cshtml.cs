using BusinessIntelligence.Modules.Search;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BusinessIntelligence.Host.Pages;

public sealed class IndexModel(CompanySearch search) : PageModel
{
    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Query { get; set; }

    [BindProperty(SupportsGet = true, Name = "judet")]
    public short? County { get; set; }

    [BindProperty(SupportsGet = true, Name = "localitate")]
    public int? Uat { get; set; }

    [BindProperty(SupportsGet = true, Name = "caen")]
    public string? Caen { get; set; }

    [BindProperty(SupportsGet = true, Name = "rev")]
    public short? CaenVersion { get; set; }

    [BindProperty(SupportsGet = true, Name = "stare")]
    public string? Lifecycle { get; set; }

    [BindProperty(SupportsGet = true, Name = "pagina")]
    public int? PageNumber { get; set; }

    // Financial bounds are read as text: "1.000.000" or "1 000 000" keep their digits instead of failing to bind.
    [BindProperty(SupportsGet = true, Name = "ca_min")]
    public string? TurnoverMin { get; set; }

    [BindProperty(SupportsGet = true, Name = "ca_max")]
    public string? TurnoverMax { get; set; }

    [BindProperty(SupportsGet = true, Name = "sal_min")]
    public string? EmployeesMin { get; set; }

    [BindProperty(SupportsGet = true, Name = "sal_max")]
    public string? EmployeesMax { get; set; }

    [BindProperty(SupportsGet = true, Name = "ordine")]
    public string? Sort { get; set; }

    /// <summary>Result orders by URL slug; the default order (relevance, or name when browsing) has no slug.</summary>
    public static readonly IReadOnlyList<(string Slug, string Value, string Label)> Sorts =
    [
        ("ca", FinancialFilter.ByTurnover, "Cifra de afaceri, descrescător"),
        ("salariati", FinancialFilter.ByEmployees, "Salariați, descrescător"),
    ];

    public SearchResult Result { get; private set; } = new();

    /// <summary>True when the URL asks for results; the bare home page shows the search box and the top-level groups.</summary>
    public bool Searching { get; private set; }

    /// <summary>A CAEN code from the URL that the selected version does not have.</summary>
    public string? UnknownCaen { get; private set; }

    public short Version => CaenVersion ?? CompanySearch.DefaultCaenVersion;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Query = string.IsNullOrWhiteSpace(Query) ? null : Query.Trim();
        if (CaenVersion is { } version && !CompanySearch.CaenVersions.Contains(version))
        {
            CaenVersion = null;
        }

        var lifecycle = CompanySearch.LifecycleLabels.FirstOrDefault(item => item.Slug == Lifecycle).Value;
        Lifecycle = lifecycle is null ? null : Lifecycle;
        Caen = string.IsNullOrWhiteSpace(Caen) ? null : Caen.Trim();
        if (County is null)
        {
            Uat = null;
        }

        TurnoverMin = Digits(TurnoverMin);
        TurnoverMax = Digits(TurnoverMax);
        EmployeesMin = Digits(EmployeesMin, 7);
        EmployeesMax = Digits(EmployeesMax, 7);
        var sort = Sorts.FirstOrDefault(item => item.Slug == Sort).Value;
        Sort = sort is null ? null : Sort;
        var financials = new FinancialFilter(Amount(TurnoverMin), Amount(TurnoverMax), (int?)Amount(EmployeesMin), (int?)Amount(EmployeesMax), sort);

        Searching = Query is not null || County is not null || Caen is not null || Lifecycle is not null || CaenVersion is not null || !financials.IsEmpty;
        Result = await search.ExecuteAsync(
            new SearchRequest(Query, County, Uat, Caen, Version, lifecycle, PageNumber ?? 1, IncludeHits: Searching, Financials: financials), cancellationToken);
        if (Result.Ready && Caen is not null && Result.CaenPath.Count == 0)
        {
            // An unknown code is dropped from the filters and named on the page, never silently matched against nothing.
            UnknownCaen = Caen;
            Caen = null;
        }
    }

    /// <summary>The digits of a bound, or null when it has none; at most 15 digits (employees: 7), so it fits the column.</summary>
    private static string? Digits(string? value, int maxLength = 15)
    {
        var digits = new string((value ?? string.Empty).Where(char.IsAsciiDigit).ToArray()).TrimStart('0');
        return value is null || !value.Any(char.IsAsciiDigit) ? null : digits.Length == 0 ? "0" : digits.Length > maxLength ? null : digits;
    }

    private static decimal? Amount(string? digits) => digits is null ? null : decimal.Parse(digits, System.Globalization.CultureInfo.InvariantCulture);

    public bool HasFinancialFilter => TurnoverMin is not null || TurnoverMax is not null || EmployeesMin is not null || EmployeesMax is not null || Sort is not null;

    public static string Lei(string digits) => decimal.Parse(digits, System.Globalization.CultureInfo.InvariantCulture).ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("ro-RO"));

    public string LifecycleLabel(string value) =>
        CompanySearch.LifecycleLabels.FirstOrDefault(item => item.Value == value).Label ?? value;

    public static string LifecycleLabelFromSlug(string slug) =>
        CompanySearch.LifecycleLabels.FirstOrDefault(item => item.Slug == slug).Label ?? slug;

    public string LifecycleSlug(string value) =>
        CompanySearch.LifecycleLabels.FirstOrDefault(item => item.Value == value).Slug ?? value;

    /// <summary>The current URL state with some values replaced; changing a filter goes back to the first page.</summary>
    public Dictionary<string, string> With(
        Change<string?> query = default, Change<short?> county = default, Change<int?> uat = default, Change<string?> caen = default,
        Change<short?> version = default, Change<string?> lifecycle = default, int? page = null,
        Change<string?> turnoverMin = default, Change<string?> turnoverMax = default, Change<string?> employeesMin = default,
        Change<string?> employeesMax = default, Change<string?> sort = default)
    {
        var values = new Dictionary<string, string?>
        {
            ["q"] = query.Or(Query),
            ["judet"] = county.Or(County)?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["localitate"] = uat.Or(Uat)?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["caen"] = caen.Or(Caen),
            ["rev"] = version.Or(CaenVersion) is { } chosen && chosen != CompanySearch.DefaultCaenVersion
                ? chosen.ToString(System.Globalization.CultureInfo.InvariantCulture) : null,
            ["stare"] = lifecycle.Or(Lifecycle),
            ["ca_min"] = turnoverMin.Or(TurnoverMin),
            ["ca_max"] = turnoverMax.Or(TurnoverMax),
            ["sal_min"] = employeesMin.Or(EmployeesMin),
            ["sal_max"] = employeesMax.Or(EmployeesMax),
            ["ordine"] = sort.Or(Sort),
            ["pagina"] = page is > 1 ? page.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : null,
        };
        return values.Where(item => item.Value is not null).ToDictionary(item => item.Key, item => item.Value!);
    }

    public readonly record struct Change<T>(T Value)
    {
        public bool HasValue { get; } = true;

        public T Or(T current) => HasValue ? Value : current;

        public static implicit operator Change<T>(T value) => new(value);
    }
}
