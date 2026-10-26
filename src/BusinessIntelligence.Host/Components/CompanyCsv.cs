using System.Globalization;
using System.Text;
using BusinessIntelligence.Modules.Risk;
using BusinessIntelligence.Modules.Search;

namespace BusinessIntelligence.Host.Components;

/// <summary>
/// Search results as CSV for spreadsheets: UTF-8 with a byte order mark, semicolons (Romanian Excel reads commas as
/// decimal separators), plain numbers without thousands separators, an empty cell for a missing value, and the sources
/// and their licenses on the last line.
/// </summary>
public static class CompanyCsv
{
    public static readonly string[] Header =
    [
        "CUI", "Nr. înregistrare", "Denumire", "Formă juridică", "Stare", "Data înregistrării", "Localitate", "Județ",
        "An financiar", "Cifra de afaceri (lei)", "Salariați", "Rezultat net (lei)", "Semnale de atenție",
    ];

    public static byte[] Write(SearchResult result, IReadOnlyDictionary<string, string[]> signals, Func<string, string> lifecycle, DateOnly exportedOn)
    {
        var text = new StringBuilder();
        Line(text, Header);
        foreach (var hit in result.Hits)
        {
            Line(text,
            [
                hit.Cui?.ToString(CultureInfo.InvariantCulture), hit.RegistrationNumber, hit.Name, hit.LegalForm, lifecycle(hit.Lifecycle),
                hit.FirstRegistrationDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), hit.Place, hit.County,
                hit.FinancialYear?.ToString(CultureInfo.InvariantCulture), hit.Turnover?.ToString(CultureInfo.InvariantCulture),
                hit.Employees?.ToString(CultureInfo.InvariantCulture), hit.NetResult?.ToString(CultureInfo.InvariantCulture),
                string.Join(", ", signals.GetValueOrDefault(hit.CompanyKey, []).Select(key => RiskSignals.Get(key).Name)),
            ]);
        }

        var shown = result.Hits.Count < result.Total
            ? $"primele {result.Hits.Count.ToString(CultureInfo.InvariantCulture)} din {result.Total.ToString(CultureInfo.InvariantCulture)} rezultate"
            : $"{result.Hits.Count.ToString(CultureInfo.InvariantCulture)} rezultate";
        Line(text,
        [
            $"Export din {exportedOn.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)}, {shown}. Surse: Oficiul Național al Registrului Comerțului (CC BY 4.0); "
            + "Ministerul Finanțelor, situațiile financiare anuale de pe data.gov.ro (CC BY 4.0 sau Open Government Licence, după an); ANAF, serviciul web de stare fiscală. "
            + "Semnalele sunt fapte din datele publice, nu un rating.",
        ]);
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text.ToString())];
    }

    private static void Line(StringBuilder text, IEnumerable<string?> cells)
    {
        text.AppendJoin(';', cells.Select(Cell));
        text.Append("\r\n");
    }

    /// <summary>Quotes a cell that holds a separator, quote or line break, doubling its quotes; a leading formula sign is neutralized.</summary>
    private static string Cell(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (value[0] is '=' or '+' or '@' or '-' && !decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
        {
            value = "'" + value;
        }

        return value.IndexOfAny([';', '"', '\r', '\n']) >= 0 ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : value;
    }
}
