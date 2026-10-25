using System.Runtime.CompilerServices;
using System.Text;

namespace BusinessIntelligence.Modules.Registry.History;

/// <summary>One registration as seen in a registry snapshot, in any of the published layouts.</summary>
public sealed record HistoryRow(
    string RegistrationNumber, string? Name, long? Cui, CuiState CuiState, string? County, string? Locality, string? Address,
    int[] StatusCodes, bool StruckOffFile);

public sealed class HistoryReadCounts
{
    public int Rows { get; set; }
    public int WithoutRegistrationNumber { get; set; }
    public int JoinedLines { get; set; }
    public int MergedExtraFields { get; set; }
}

/// <summary>
/// Reads the 2013–2024 ONRC snapshots, published as separate files for active and struck-off registrations, with and
/// without a registered office, some split into parts. Layouts differ by year (see S02.4 inspection): '|' or '^',
/// optional header, quoted values (2016), free-text address (2017–2020) or structured columns (2021–2024). The reader
/// follows the header when there is one; 2013 has none and uses the 2014 column order.
/// </summary>
public static class LegacySnapshotReader
{
    public const string ParserVersion = "onrc-history-v1";

    private static readonly string[] HeaderlessWithOffice = ["DENUMIRE", "CUI", "COD_INMATRICULARE", "STARE_FIRMA", "JUDET", "LOCALITATE"];
    private static readonly string[] HeaderlessWithoutOffice = ["DENUMIRE", "CUI", "COD_INMATRICULARE", "STARE_FIRMA"];

    /// <summary>Parts of one kind (active or struck off, with or without office), in part order.</summary>
    public static async IAsyncEnumerable<HistoryRow> ReadAsync(
        IReadOnlyList<Stream> parts, bool struckOff, bool withOffice, HistoryReadCounts counts,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string[]? header = null;
        var separator = '\0';
        string[]? held = null;
        string? pending = null;
        foreach (var part in parts)
        {
            // A few 2022–2024 fields are cut inside a multi-byte character by the publisher; those bytes become U+FFFD.
            using var reader = new StreamReader(part, new UTF8Encoding(false, false), true, 1 << 16, leaveOpen: true);
            while (await reader.ReadLineAsync(cancellationToken) is { } raw)
            {
                var line = raw.TrimStart('\uFEFF');
                if (line.Length == 0)
                {
                    continue;
                }

                if (separator == '\0')
                {
                    separator = line.Count(character => character == '^') > line.Count(character => character == '|') ? '^' : '|';
                }

                if (line.StartsWith("DENUMIRE", StringComparison.Ordinal))
                {
                    header ??= line.Split(separator).Select(cell => cell.Trim()).Where(cell => cell.Length > 0).ToArray();
                    continue;
                }

                header ??= withOffice ? HeaderlessWithOffice : HeaderlessWithoutOffice;
                var text = pending is null ? line : pending + " " + line;
                var fields = text.Split(separator);
                if (fields.Length >= header.Length)
                {
                    pending = null;
                    if (held is not null && Parse(held, header, struckOff, counts) is { } previous)
                    {
                        yield return previous;
                    }

                    held = fields;
                }
                else if (pending is null && held is not null && !StartsRecord(fields))
                {
                    // An address with a line break: the rest of the previous record's last field.
                    counts.JoinedLines++;
                    held[^1] += " " + line;
                }
                else
                {
                    // A record broken before its last field: wait for the rest.
                    counts.JoinedLines += pending is null ? 0 : 1;
                    pending = text;
                }
            }
        }

        if (held is not null && Parse(held, header!, struckOff, counts) is { } last)
        {
            yield return last;
        }
    }

    /// <summary>A record starts with a name, a CUI and a registry number such as J12/1/2001, F12/5/2010 or J2024004913234.</summary>
    private static bool StartsRecord(string[] fields) =>
        fields.Length >= 3 && fields[2].Trim().Trim('"') is { Length: > 3 } number && number[0] is 'J' or 'F' or 'C' && char.IsAsciiDigit(number[1]);

    private static HistoryRow? Parse(string[] fields, string[] header, bool struckOff, HistoryReadCounts counts)
    {
        var length = fields.Length;
        while (length > header.Length && fields[length - 1].Trim().Length == 0)
        {
            // 2022–2024 rows often end with an extra empty field.
            length--;
        }

        if (length > header.Length)
        {
            // A separator inside the last, free-text column (2017 addresses contain '|').
            counts.MergedExtraFields++;
            fields[header.Length - 1] = string.Join(" ", fields[(header.Length - 1)..length]);
        }

        string? Field(string name)
        {
            var index = Array.IndexOf(header, name);
            if (index < 0)
            {
                return null;
            }

            var value = fields[index].Trim().Trim('"').Trim();
            return value.Length == 0 ? null : value;
        }

        counts.Rows++;
        var registrationNumber = Field("COD_INMATRICULARE");
        if (registrationNumber is null)
        {
            counts.WithoutRegistrationNumber++;
            return null;
        }

        var (cuiState, cui) = RomanianCui.Parse(Field("CUI") ?? string.Empty);
        var codes = (Field("STARE_FIRMA") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(code => int.TryParse(code, out var number) ? number : -1).Where(code => code > 0).Distinct().Order().ToArray();

        string? county = Field("ADR_JUDET") ?? Field("JUDET");
        string? locality = Field("ADR_LOCALITATE") ?? Field("LOCALITATE");
        var address = Field("ADRESA_COMPLETA") ?? Field("ADRESA");
        if (county is null && locality is null && Field("ADRESA") is { } freeText)
        {
            (county, locality) = AddressText.Split(freeText);
        }

        return new HistoryRow(registrationNumber, Field("DENUMIRE"), cui, cuiState, county, locality,
            address ?? JoinAddress(county, locality), codes, struckOff);
    }

    private static string? JoinAddress(string? county, string? locality) =>
        county is null && locality is null ? null : string.Join(", ", new[] { locality, county }.Where(part => part is not null));
}

/// <summary>Extracts county and locality from the 2017–2020 free-text addresses ("Sat X, Comuna Y, Str. …, Județ Z").</summary>
public static class AddressText
{
    private static readonly string[] AdministrativeUnits = ["Comuna ", "Oraş ", "Oraș ", "Municipiul ", "Com. "];

    public static (string? County, string? Locality) Split(string address)
    {
        var segments = address.Split(", ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return (null, null);
        }

        var countySegment = segments.LastOrDefault(segment => segment.StartsWith("Județ ", StringComparison.Ordinal)
            || segment.StartsWith("Judeţ ", StringComparison.Ordinal));
        var county = countySegment?[6..].Trim();
        var locality = segments[0];
        if (locality.StartsWith("Bucureşti Sectorul", StringComparison.Ordinal) || locality.StartsWith("București Sectorul", StringComparison.Ordinal))
        {
            return (county ?? "Bucureşti", locality);
        }

        if (segments.Length > 1 && AdministrativeUnits.Any(unit => segments[1].StartsWith(unit, StringComparison.Ordinal)))
        {
            locality += ", " + segments[1];
        }

        return (county, locality);
    }
}
