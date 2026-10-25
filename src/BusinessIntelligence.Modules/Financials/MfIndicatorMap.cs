using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace BusinessIntelligence.Modules.Financials;

/// <summary>
/// Maps the columns of one MF financial statements file to the shared indicators, using its dictionary
/// (<c>label;key;</c>, UTF-8 or Windows-1250). Labels decide the meaning because positions shift between layouts.
/// </summary>
public static partial class MfIndicatorMap
{
    public const string FixedAssets = nameof(FinancialStatement.FixedAssets);
    public const string GrossLoss = nameof(FinancialStatement.GrossLoss);
    public const string NetLoss = nameof(FinancialStatement.NetLoss);

    private static readonly Dictionary<string, string> Labels = new(StringComparer.Ordinal)
    {
        ["ACTIVE IMOBILIZATE TOTAL"] = FixedAssets,
        ["ACTIVE CIRCULANTE TOTAL DIN CARE"] = nameof(FinancialStatement.CurrentAssets),
        ["STOCURI"] = nameof(FinancialStatement.Inventories),
        ["STOCURI TOTAL"] = nameof(FinancialStatement.Inventories),
        ["CREANTE"] = nameof(FinancialStatement.Receivables),
        ["CASA SI CONTURI LA BANCI"] = nameof(FinancialStatement.Cash),
        ["CHELTUIELI IN AVANS"] = nameof(FinancialStatement.PrepaidExpenses),
        ["DATORII"] = nameof(FinancialStatement.Liabilities),
        ["VENITURI IN AVANS"] = nameof(FinancialStatement.DeferredIncome),
        ["PROVIZIOANE"] = nameof(FinancialStatement.Provisions),
        ["CAPITALURI TOTAL DIN CARE"] = nameof(FinancialStatement.Equity),
        ["CAPITAL SUBSCRIS VARSAT"] = nameof(FinancialStatement.ShareCapital),
        ["CAPITAL"] = nameof(FinancialStatement.ShareCapital),
        ["PATRIMONIUL REGIEI"] = nameof(FinancialStatement.RegiePatrimony),
        ["PATRIMONIUL PUBLIC"] = nameof(FinancialStatement.PublicPatrimony),
        ["CIFRA DE AFACERI NETA"] = nameof(FinancialStatement.Turnover),
        ["VENITURI TOTALE"] = nameof(FinancialStatement.TotalIncome),
        ["CHELTUIELI TOTALE"] = nameof(FinancialStatement.TotalExpenses),
        ["PROFIT BRUT"] = nameof(FinancialStatement.GrossProfit),
        ["PROFITUL BRUT"] = nameof(FinancialStatement.GrossProfit),
        ["PIERDERE BRUTA"] = GrossLoss,
        ["PROFIT NET"] = nameof(FinancialStatement.NetProfit),
        ["PROFITUL NET"] = nameof(FinancialStatement.NetProfit),
        ["PIERDERE NETA"] = NetLoss,
        ["NUMAR MEDIU DE SALARIATI"] = nameof(FinancialStatement.Employees),
    };

    [GeneratedRegex(@"^i?(\d{1,2})$", RegexOptions.IgnoreCase)]
    private static partial Regex IndicatorKey();

    /// <summary>Returns the indicator for each data column I1…In, in order, and the dictionary defects that were corrected.</summary>
    public static (IReadOnlyList<string> Columns, IReadOnlyList<string> Corrections) Parse(byte[] content)
    {
        var text = Decode(content);
        var columns = new SortedDictionary<int, string>();
        var corrections = new List<string>();
        foreach (var line in text.Split('\n').Select(line => line.Trim('\r', ' ')).Where(line => line.Length > 0))
        {
            var cells = line.Split(';');
            if (cells.Length < 2)
            {
                throw new InvalidDataException($"MF dictionary: unexpected line '{line}'.");
            }

            var key = cells[1].Trim();
            if (key.Equals("cui", StringComparison.OrdinalIgnoreCase) || key.Equals("caen", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var match = IndicatorKey().Match(key);
            if (!match.Success)
            {
                throw new InvalidDataException($"MF dictionary: unexpected key '{key}'.");
            }

            if (!key.StartsWith('i') && !key.StartsWith('I'))
            {
                corrections.Add($"key '{key}' read as 'i{match.Groups[1].Value}'");
            }

            var label = Fold(cells[0]);
            if (!Labels.TryGetValue(label, out var indicator))
            {
                throw new InvalidDataException($"MF dictionary: unknown label '{cells[0].Trim()}'.");
            }

            columns.Add(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), indicator);
        }

        if (columns.Count == 0 || columns.Keys.First() != 1 || columns.Keys.Last() != columns.Count)
        {
            throw new InvalidDataException("MF dictionary: indicator keys are not a contiguous I1…In sequence.");
        }

        var result = columns.Values.ToList();
        var netLosses = result.Select((indicator, index) => (indicator, index)).Where(item => item.indicator == NetLoss).ToList();
        if (netLosses.Count == 2 && !result.Contains(GrossLoss))
        {
            // UU 2014–2015 label the gross loss column "PIERDERE NETA"; it is the one before net profit.
            result[netLosses[0].index] = GrossLoss;
            corrections.Add($"i{netLosses[0].index + 1} 'PIERDERE NETA' read as gross loss");
        }

        var duplicate = result.GroupBy(indicator => indicator).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidDataException($"MF dictionary: {duplicate.Key} appears twice.");
        }

        return (result, corrections);
    }

    internal static string Fold(string label)
    {
        var decomposed = label.Normalize(NormalizationForm.FormKD);
        var builder = new StringBuilder(decomposed.Length);
        var space = false;
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToUpperInvariant(character));
                space = false;
            }
            else if (!space && builder.Length > 0)
            {
                builder.Append(' ');
                space = true;
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static string Decode(byte[] content)
    {
        try
        {
            return new UTF8Encoding(false, true).GetString(content).TrimStart('﻿');
        }
        catch (DecoderFallbackException)
        {
            // The IR dictionaries from 2016 are Windows-1250.
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1250).GetString(content);
        }
    }
}
