using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace BusinessIntelligence.Modules.Registry.Geography;

/// <summary>
/// States: Locality, Sector, Uat (resolved); UatOnly (locality not found inside a resolved UAT); LocalityAmbiguous, UatAmbiguous,
/// UatUnmatched, Unmatched, LocalityNotGiven, CountyUnmatched. Method is Exact or Orthographic.
/// </summary>
public sealed record SeatGeographyMatch(string State, string Method, short? CountyNumber, int? UatCode, int? LocalityCode);

// Ported from the original repository (D03.6), where it resolved 99.89% of registry seats.
// Deterministic, explainable matching of the registry's county and locality text to SIRUTA codes.
// A code is assigned only when exactly one place has the folded name in its scope; several candidates give an Ambiguous state, none gives Unmatched.
// Folding removes diacritics, case and punctuation. The orthographic pass additionally treats the old i-circumflex spelling as a-circumflex
// (Sîngeorgiu and Sângeorgiu) and is reported as such, never as an exact match.
public sealed partial class SirutaSeatMatcher
{
    private readonly Dictionary<string, short> _counties = [];
    private readonly Dictionary<string, short> _countiesOrthographic = [];
    private readonly Dictionary<(short County, string Name), List<int>> _uats = [];
    private readonly Dictionary<(short County, string Name), List<int>> _uatsOrthographic = [];
    private readonly Dictionary<(int Parent, string Name), List<int>> _children = [];
    private readonly Dictionary<(int Parent, string Name), List<int>> _childrenOrthographic = [];
    private readonly Dictionary<(short County, string Name), List<int>> _countyLocalities = [];
    private readonly Dictionary<int, SirutaPlace> _places = [];

    // Administrative type words that SIRUTA puts in front of county and UAT names. Localities keep their names untouched (a village can be called Sat-Sugatag).
    [GeneratedRegex(@"^(JUDETUL|MUNICIPIUL|ORASUL|ORAS|COMUNA)\s+")]
    private static partial Regex TypePrefix();

    [GeneratedRegex(@"^(COMUNA|ORAS|ORASUL|MUNICIPIUL)\s+")]
    private static partial Regex UatPrefix();

    [GeneratedRegex(@"^(SAT|LOC)\s+")]
    private static partial Regex LocalityPrefix();

    public SirutaSeatMatcher(IEnumerable<SirutaPlace> places)
    {
        foreach (var place in places)
        {
            _places[place.Code] = place;
            var name = place.Level == 3 ? Fold(place.Name) : Strip(Fold(place.Name));
            var orthographic = place.Level == 3 ? Fold(place.Name, true) : Strip(Fold(place.Name, true));
            if (place.Level == 1)
            {
                _counties[name] = place.CountyNumber;
                _countiesOrthographic[orthographic] = place.CountyNumber;
            }
            else if (place.Level == 2)
            {
                Add(_uats, (place.CountyNumber, name), place.Code);
                Add(_uatsOrthographic, (place.CountyNumber, orthographic), place.Code);
            }
            else if (place.Level == 3)
            {
                Add(_children, (place.ParentCode, name), place.Code);
                Add(_childrenOrthographic, (place.ParentCode, orthographic), place.Code);
                Add(_countyLocalities, (place.CountyNumber, name), place.Code);
            }
        }
    }

    public SeatGeographyMatch Match(string? county, string? locality)
    {
        var countyText = (county ?? "").Trim();
        if (countyText.Length == 0) { return new("CountyUnmatched", "Exact", null, null, null); }
        var method = "Exact";
        if (!_counties.TryGetValue(Strip(Fold(countyText)), out var countyCode))
        {
            if (!_countiesOrthographic.TryGetValue(Strip(Fold(countyText, true)), out countyCode)) { return new("CountyUnmatched", "Exact", null, null, null); }
            method = "Orthographic";
        }

        var text = (locality ?? "").Trim();
        if (text.Length == 0 || text.StartsWith("***", StringComparison.Ordinal)) { return new("LocalityNotGiven", method, countyCode, null, null); }
        var result = Resolve(countyCode, text, false);
        if (result.State is "Unmatched" or "UatUnmatched" or "UatOnly")
        {
            // The orthographic pass replaces the first one only when it resolves more (a place or a UAT); it never turns a match into a worse one.
            var second = Resolve(countyCode, text, true);
            if (second.State is "Locality" or "Uat" or "Sector") { result = second; }
        }

        return result.Method == "Orthographic" || method == "Exact" ? result : result with { Method = "Orthographic" };
    }

    private SeatGeographyMatch Resolve(short countyCode, string text, bool orthographic)
    {
        var method = orthographic ? "Orthographic" : "Exact";
        var uats = orthographic ? _uatsOrthographic : _uats;
        var children = orthographic ? _childrenOrthographic : _children;
        var comma = text.IndexOf(", ", StringComparison.Ordinal);
        if (comma > 0)
        {
            var left = LocalityPrefix().Replace(Fold(text[..comma], orthographic), "");
            var right = UatPrefix().Replace(Fold(text[(comma + 2)..], orthographic), "");
            if (!uats.TryGetValue((countyCode, right), out var found)) { return new("UatUnmatched", method, countyCode, null, null); }
            if (found.Count > 1) { return new("UatAmbiguous", method, countyCode, null, null); }
            if (!children.TryGetValue((found[0], left), out var places)) { return new("UatOnly", method, countyCode, found[0], null); }
            return places.Count == 1 ? new("Locality", method, countyCode, found[0], places[0]) : new("LocalityAmbiguous", method, countyCode, found[0], null);
        }

        var folded = Fold(text, orthographic);
        if (SectorPattern().IsMatch(folded) && !orthographic)
        {
            var sector = _places.Values.Where(place => place.Level == 3 && place.CountyNumber == countyCode && Fold(place.Name) == folded).ToArray();
            if (sector.Length == 1) { return new("Sector", method, countyCode, sector[0].ParentCode, sector[0].Code); }
        }

        var name = UatPrefix().Replace(folded, "");
        if (uats.TryGetValue((countyCode, name), out var single))
        {
            return single.Count == 1 ? new("Uat", method, countyCode, single[0], null) : new("UatAmbiguous", method, countyCode, null, null);
        }

        // A bare locality name: accepted only when one locality in the county has it.
        if (!orthographic && _countyLocalities.TryGetValue((countyCode, name), out var locals) && locals.Count == 1)
        {
            return new("Locality", method, countyCode, _places[locals[0]].ParentCode, locals[0]);
        }

        return new("Unmatched", method, countyCode, null, null);
    }

    [GeneratedRegex(@"^BUCURESTI SECTORUL \d$")]
    private static partial Regex SectorPattern();

    private static void Add<TKey>(Dictionary<TKey, List<int>> map, TKey key, int code) where TKey : notnull
    {
        if (!map.TryGetValue(key, out var list)) { map[key] = list = []; }
        list.Add(code);
    }

    private static string Strip(string folded) => TypePrefix().Replace(folded, "");

    internal static string Fold(string value, bool orthographic = false)
    {
        if (orthographic) { value = value.Replace('î', 'â').Replace('Î', 'Â'); }
        var decomposed = value.Normalize(NormalizationForm.FormKD);
        var builder = new StringBuilder(decomposed.Length);
        var space = false;
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) { continue; }
            if (char.IsAsciiLetterOrDigit(character) || char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToUpperInvariant(character));
                space = false;
            }
            else if (!space && builder.Length > 0) { builder.Append(' '); space = true; }
        }

        return builder.ToString().TrimEnd();
    }
}
