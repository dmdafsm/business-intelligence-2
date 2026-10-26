using System.Globalization;
using System.Text.RegularExpressions;
using BusinessIntelligence.Modules.Registry;
using BusinessIntelligence.Modules.Registry.History;

namespace BusinessIntelligence.Modules.Search;

/// <summary>
/// What a search box entry can be: a CUI, a registry number and always a name. <see cref="Words"/> are the name's
/// words without legal forms; each must start a word of a matching name.
/// </summary>
public sealed record SearchQuery(long? Cui, string? RegistrationNumber, string? NameKey, IReadOnlyList<string> Words);

/// <summary>Keys shared by the index build and the queries, so both sides fold text the same way.</summary>
public static partial class SearchKeys
{
    public const int MaxWords = 8;

    /// <summary>Legal-form words (after <see cref="ImportRegistryHistory.NameKey"/>) that half the registry shares; they do not narrow a search.</summary>
    private static readonly HashSet<string> LegalForms = ["SRL", "SA", "SC", "PFA", "II", "IF", "SNC", "SCS", "SCA", "RA"];

    /// <summary>
    /// The registry name key (folded, legal-form spellings unified) without a leading "SC", which older names carry and
    /// newer ones dropped. Null when nothing searchable is left.
    /// </summary>
    public static string? Name(string? name)
    {
        var key = ImportRegistryHistory.NameKey(name);
        if (key is null)
        {
            return null;
        }

        key = StripCompanyPrefix(key);
        return key.Length == 0 ? null : key;
    }

    /// <summary><see cref="StripCompanyPrefix(string)"/> as SQL over an existing name key column.</summary>
    public static string StripCompanyPrefixSql(string column) => $"regexp_replace({column}, '^S ?C ', '')";

    public static string StripCompanyPrefix(string key) =>
        key.StartsWith("SC ", StringComparison.Ordinal) ? key[3..]
        : key.StartsWith("S C ", StringComparison.Ordinal) ? key[4..]
        : key;

    /// <summary>
    /// Normalizes a registry number: J40/0012/2001 and J40/12/2001 are the same; J08/5/2010 and J8/5/2010 too. Numbers
    /// in the newer form (J2024000100123) are kept as written. Null when the text is not shaped like a registry number.
    /// </summary>
    public static string? RegistrationNumber(string? text)
    {
        if (text is null)
        {
            return null;
        }

        var compact = Whitespace().Replace(text, string.Empty).ToUpperInvariant();
        var old = OldNumber().Match(compact);
        if (old.Success)
        {
            return string.Create(CultureInfo.InvariantCulture,
                $"{old.Groups[1].Value}{int.Parse(old.Groups[2].Value, CultureInfo.InvariantCulture)}/{long.Parse(old.Groups[3].Value, CultureInfo.InvariantCulture)}/{old.Groups[4].Value}");
        }

        return NewNumber().IsMatch(compact) ? compact : null;
    }

    /// <summary><see cref="RegistrationNumber(string)"/> as SQL over a stored registry number column.</summary>
    public static string RegistrationNumberSql(string column) => """
        CASE WHEN $n ~ '^[A-Z][0-9]{1,2}/[0-9]{1,9}/[0-9]{4}$'
             THEN left($n, 1) || split_part(substr($n, 2), '/', 1)::int || '/' || split_part($n, '/', 2)::bigint || '/' || split_part($n, '/', 3)
             ELSE upper($n) END
        """.Replace("$n", column, StringComparison.Ordinal);

    public static SearchQuery Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new SearchQuery(null, null, null, []);
        }

        long? cui = null;
        var compact = Whitespace().Replace(text, string.Empty);
        if (compact.StartsWith("RO", StringComparison.OrdinalIgnoreCase))
        {
            compact = compact[2..];
        }

        if (compact.Length > 0 && compact.All(char.IsAsciiDigit) && RomanianCui.Parse(compact) is (CuiState.Valid, var value))
        {
            cui = value;
        }

        var key = Name(text);
        var words = key is null ? [] : key.Split(' ').Where(word => !LegalForms.Contains(word)).Distinct(StringComparer.Ordinal).Take(MaxWords).ToArray();
        return new SearchQuery(cui, RegistrationNumber(text), key, words);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"^([A-Z])0*(\d{1,2})/0*(\d{1,9})/(\d{4})$")]
    private static partial Regex OldNumber();

    [GeneratedRegex(@"^[A-Z]\d{13}$")]
    private static partial Regex NewNumber();
}
