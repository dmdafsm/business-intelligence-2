using System.Globalization;
using System.Text;

namespace BusinessIntelligence.Modules.Registry.Onrc;

/// <summary>Parsers for the small ONRC nomenclature files (UTF-8 with BOM, '^'-delimited, header row).</summary>
public static class OnrcNomenclatures
{
    private static readonly string[] CaenHeader = ["SECTIUNEA", "SUBSECTIUNEA", "DIVIZIUNEA", "GRUPA", "CLASA", "DENUMIRE", "VERSIUNE_CAEN"];
    private static readonly string[] StatusHeader = ["COD", "DENUMIRE"];

    /// <summary>
    /// Returns one entry per (version, code). A code repeated with a different name is a source defect (the 2003 version
    /// labels Agriculture with section D): the first row is kept and the repeat is reported, never silently merged.
    /// </summary>
    public static (IReadOnlyList<CaenEntry> Entries, IReadOnlyList<string> Defects) ParseCaen(byte[] content)
    {
        var entries = new Dictionary<(short, string), CaenEntry>();
        var defects = new List<string>();
        foreach (var fields in Rows(content, CaenHeader))
        {
            var section = fields[0].Trim();
            var subsection = Code(fields[1], 2);
            var division = Code(fields[2], 2);
            var group = Code(fields[3], 3);
            var classCode = Code(fields[4], 4);
            if (section.Length == 0 || !short.TryParse(fields[6].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var version))
            {
                throw new InvalidDataException($"N_CAEN: row without section or version: {string.Join('^', fields)}");
            }

            // The most specific code present decides the level; Rev.2 rows repeat parent codes in the class column.
            var (level, code) = classCode is not null ? ("class", classCode)
                : group is not null ? ("group", group)
                : division is not null ? ("division", division)
                : subsection is not null ? ("subsection", subsection)
                : ("section", section);
            var name = fields[5].Trim();
            var entry = new CaenEntry
            {
                Version = version,
                Code = code,
                Level = level,
                Section = section,
                Division = division,
                Group = group,
                Name = name.Length == 0 ? null : name,
            };
            if (!entries.TryAdd((version, code), entry) && entries[(version, code)].Name != entry.Name)
            {
                defects.Add($"version {version} {level} {code}: repeated as \"{entry.Name}\"; kept \"{entries[(version, code)].Name}\"");
            }
        }

        return ([.. entries.Values], defects);
    }

    public static IReadOnlyList<CompanyStatusCode> ParseStatusCodes(byte[] content)
    {
        var codes = new Dictionary<int, CompanyStatusCode>();
        foreach (var fields in Rows(content, StatusHeader))
        {
            if (!int.TryParse(fields[0].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var code) || fields[1].Trim().Length == 0)
            {
                throw new InvalidDataException($"N_STARE_FIRMA: invalid row {string.Join('^', fields)}");
            }

            if (!codes.TryAdd(code, new CompanyStatusCode { Code = code, Name = fields[1].Trim() }))
            {
                throw new InvalidDataException($"N_STARE_FIRMA: duplicate code {code}.");
            }
        }

        return [.. codes.Values];
    }

    private static string? Code(string value, int length)
    {
        var trimmed = value.Trim();
        return trimmed.Length == length ? trimmed : null;
    }

    private static IEnumerable<string[]> Rows(byte[] content, string[] header)
    {
        var text = new UTF8Encoding(false, true).GetString(content).TrimStart('﻿');
        var lines = text.Split('\n');
        if (!lines[0].TrimEnd('\r').Split('^').SequenceEqual(header))
        {
            throw new InvalidDataException("Unexpected nomenclature header: " + lines[0]);
        }

        foreach (var line in lines.Skip(1))
        {
            var trimmed = line.TrimEnd('\r');
            if (trimmed.Length == 0)
            {
                continue;
            }

            var fields = trimmed.Split('^');
            if (fields.Length != header.Length)
            {
                throw new InvalidDataException($"Expected {header.Length} fields: {trimmed}");
            }

            yield return fields;
        }
    }
}
