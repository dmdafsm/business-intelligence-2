using System.Globalization;
using System.Text;

namespace BusinessIntelligence.Modules.Search;

/// <summary>SIRUTA writes names in capitals with cedillas (MUNICIPIUL CLUJ-NAPOCA, JUDEŢUL ARGEŞ); pages show them in sentence form.</summary>
public static class PlaceNames
{
    private static readonly HashSet<string> Lowercase = ["de", "din", "la", "pe", "cu", "sub", "lui"];
    private static readonly string[] CountyPrefixes = ["Județul ", "Municipiul "];

    public static string Display(string name, bool county = false)
    {
        var text = name.Trim().ToLower(CultureInfo.GetCultureInfo("ro-RO"))
            .Replace('ş', 'ș').Replace('ţ', 'ț');
        var builder = new StringBuilder(text.Length);
        var word = new StringBuilder();
        foreach (var character in text)
        {
            if (character is ' ' or '-' or '.' or '(')
            {
                builder.Append(Word(word.ToString(), builder.Length == 0)).Append(character);
                word.Clear();
                continue;
            }

            word.Append(character);
        }

        builder.Append(Word(word.ToString(), builder.Length == 0));
        var display = builder.ToString();
        if (county)
        {
            foreach (var prefix in CountyPrefixes.Where(prefix => display.StartsWith(prefix, StringComparison.Ordinal)))
            {
                display = display[prefix.Length..];
            }
        }

        return display;
    }

    private static string Word(string word, bool first) =>
        word.Length == 0 || (!first && Lowercase.Contains(word)) ? word : char.ToUpper(word[0], CultureInfo.GetCultureInfo("ro-RO")) + word[1..];
}
