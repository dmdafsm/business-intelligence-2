using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using BusinessIntelligence.Modules.Platform.Files;
using BusinessIntelligence.Modules.Registry.Geography;

namespace BusinessIntelligence.Modules.Procurement;

/// <summary>One award as read from any SEAP export layout, as published text.</summary>
public sealed record SeapRow(int Row, string? AuthorityName, string? AuthorityCui, string? Reference, string? Procedure, string? ContractType,
    string? CpvCode, string? CpvName, string? Title, string? Lot, string? ContractNumber, string? Published, string? ContractDate, string? Value,
    string? EuFunded, string? Programme, string? WinnerName, string? WinnerCui);

/// <summary>
/// Reads SEAP exports of every year (2007–2026): CSV (any of ^ | , ; delimiters, quoted with " or |, lines in UTF-8 or
/// Windows-1250), .xlsx, old binary .xls and .ods. Columns are found by their header label, compared folded and
/// without punctuation, through the aliases each layout used; a label repeated in one header is resolved by position.
/// </summary>
public static class SeapRows
{
    private const int Columns = 80;
    private static readonly char[] Delimiters = ['^', '|', ',', ';', '\t'];

    /// <summary>What was learnt about a file while reading it.</summary>
    public sealed class FileFormat
    {
        public string Layout { get; set; } = string.Empty;
        public int Windows1250Lines { get; set; }

        /// <summary>Whether slash dates are day first; null until a date with a part over 12 shows it (month first by default).</summary>
        public bool? DayFirst { get; set; }

        /// <summary>CSV rows with more fields than the header, realigned by <see cref="Realign"/>.</summary>
        public int RealignedRows { get; set; }

        /// <summary>CSV rows whose field count differs from the header and could not be realigned; not imported.</summary>
        public int SkippedRows { get; set; }
    }

    /// <summary>Field → header labels (normalized), most specific first.</summary>
    private static readonly (string Field, string[] Labels)[] Aliases =
    [
        ("authorityName", ["AUTORITATECONTRACTANTA", "DENUMIREAC", "NUMEAC"]),
        ("authorityCui", ["AUTORITATECONTRACTANTACUI", "AUTORITATECONTRACTANTACU", "CUIAUTORITATECONTRACTANTA", "CUIAUTORITATECONRACTANTA", "CUIAC", "CUI"]),
        ("reference", ["NUMARANUNTATRIBUIRE", "NUMARACHIZITIEDIRECTA", "NUMARACHIZITIE", "NUMARACHIZITE", "NUMARANUNT"]),
        ("procedure", ["TIPPROCEDURA"]),
        ("contractType", ["TIPCONTRACT"]),
        ("cpvCode", ["CPVCODE", "CODCPV"]),
        ("cpvName", ["DENUMIRECPV", "NUMECPV"]),
        ("title", ["TITLUCONTRACT", "DENUMIRECONTRACT", "DENUMIREACHIZITIE", "DENUMIRE"]),
        ("lot", ["NUMARLOT"]),
        ("contractNumber", ["NUMARCONTRACT"]),
        ("published", ["DATAANUNTATRIBUIRE", "DATAPUBLICAREANUNTATRIBUIRE", "DATAPUBLICAREACHIZITIE", "DATAANUNT", "DATAPUBLICARE"]),
        ("contractDate", ["DATACONTRACT", "DATAPUBLICARECONTRACT", "DATAATRIBUIREACHIZITIE", "DATAFINALIZARE"]),
        ("value", ["VALOARECONTRACTRON", "VALOAREATRIBUITARON", "VALOAREACHIZITIERON", "VALOARERON", "VALOAREACHIZITIE"]),
        ("euFunded", ["FINANTAREPRINFONDURICOMUNITARE", "FONDURICOMUNITARE"]),
        ("programme", ["DENUMIREPROGRAMPROIECT", "DENUMIREPROGRAM"]),
        ("winnerName", ["OFERTANTCASTIGATOR", "CASTIGATOR", "CATIGATOR", "OFERTANT"]),
        ("winnerCui", ["CUIOFERTANTCASTIGATOR", "CASTIGATORCUI", "CUICASTIGATOR", "CUIOFERTANT", "CUIOF"]),
    ];

    private static readonly HashSet<string> Known = [.. Aliases.SelectMany(alias => alias.Labels)];

    public static string Normalize(string? label)
    {
        if (label is null)
        {
            return string.Empty;
        }

        try
        {
            return new string([.. SirutaSeatMatcher.Fold(label).Where(char.IsAsciiLetterOrDigit)]);
        }
        catch (ArgumentException)
        {
            // Text that Unicode normalization refuses is not a header label.
            return string.Empty;
        }
    }

    public static async IAsyncEnumerable<SeapRow> ReadAsync(string path, bool contracts, FileFormat format, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Dictionary<string, int>? map = null;
        string[] labels = [];
        await foreach (var (row, raw, delimited) in CellsAsync(path, format, cancellationToken))
        {
            var cells = raw;
            if (map is null)
            {
                if (row > 6)
                {
                    throw new InvalidDataException("No header row with the expected SEAP columns in the first rows.");
                }

                labels = [.. cells.Select(Normalize)];
                if (labels.Count(Known.Contains) >= 4)
                {
                    map = Map(labels, contracts);
                    if (!map.ContainsKey("value") || !(map.ContainsKey("winnerName") || map.ContainsKey("winnerCui")))
                    {
                        throw new InvalidDataException("The header has no value or winner column: " + string.Join(" | ", labels));
                    }

                    format.Layout = string.Join(",", labels.Where(label => label.Length > 0));
                }

                continue;
            }

            // More fields than the header means unquoted delimiters in free text (or, in 2022 Q2, contract rows appended to
            // the direct purchases): realign, or skip and count the row.
            if (delimited && cells.Length > labels.Length)
            {
                if (Realign(cells, labels) is not { } aligned)
                {
                    format.SkippedRows++;
                    continue;
                }

                cells = aligned;
                format.RealignedRows++;
            }

            // A repeated header (the machine labels of 2025 contract exports) and blank rows.
            if (cells.Count(cell => Known.Contains(Normalize(cell))) >= 4 || cells.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            string? Cell(string field) => map.TryGetValue(field, out var index) && index < cells.Length && !string.IsNullOrWhiteSpace(cells[index]) ? cells[index]!.Trim() : null;
            if (Cell("winnerName") is null && Cell("winnerCui") is null && Cell("authorityName") is null)
            {
                continue;
            }

            yield return new SeapRow(row, Cell("authorityName"), Cell("authorityCui"), Cell("reference"), Cell("procedure"), Cell("contractType"),
                Cell("cpvCode"), Cell("cpvName"), Cell("title"), Cell("lot"), Cell("contractNumber"), Cell("published"), Cell("contractDate"),
                Cell("value"), Cell("euFunded"), Cell("programme"), Cell("winnerName"), Cell("winnerCui"));
        }

        if (map is null)
        {
            throw new InvalidDataException("The file is empty or has no header row.");
        }
    }

    private static Dictionary<string, int> Map(string[] labels, bool contracts)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (field, aliases) in Aliases)
        {
            foreach (var alias in aliases)
            {
                var index = Array.IndexOf(labels, alias);
                if (index >= 0)
                {
                    map[field] = index;
                    break;
                }
            }
        }

        // Contract exports from 2024 repeat "Data publicare": the one after the award notice number is the award's date.
        if (contracts && map.TryGetValue("reference", out var reference) && labels[reference] == "NUMARANUNTATRIBUIRE"
            && reference + 1 < labels.Length && labels[reference + 1] == "DATAPUBLICARE")
        {
            map["published"] = reference + 1;
        }

        return map;
    }

    /// <summary>
    /// Realigns a row of the 2007–2016 contract layout written with unquoted commas in free text (2016 H1): the address,
    /// the authority's name and the contract title may span several fields. Anchors: the award type ("Anunt de atribuire…"),
    /// the authority's CUI, and the value, currency and value in lei. Returns null for other layouts or when an anchor is missing.
    /// </summary>
    public static string?[]? Realign(string?[] fields, string[] labels)
    {
        if (fields.Length <= labels.Length || Array.IndexOf(labels, "TIPAC") != 10 || Array.IndexOf(labels, "TIP") != 5)
        {
            return null;
        }

        var type = Array.FindIndex(fields, 5, field => field?.StartsWith("Anunt de atribuire", StringComparison.Ordinal) == true);
        var cui = type < 0 ? -1 : Array.FindIndex(fields, type + 4, field => field is not null && System.Text.RegularExpressions.Regex.IsMatch(field.Trim(), @"^(RO)?\s*\d{2,10}$"));
        if (cui < 0 || cui + 3 >= fields.Length)
        {
            return null;
        }

        // The announcement and contract dates anchor the middle: the fields between them are free text with commas too.
        var announced = Array.FindIndex(fields, cui + 1, IsDate);
        var signed = announced < 0 ? -1 : Array.FindIndex(fields, announced + 1, IsDate);
        if (announced < 1 || signed < 0)
        {
            return null;
        }

        // The value, its currency and the value in lei follow the title.
        var value = -1;
        for (var index = signed + 1; index + 2 < fields.Length; index++)
        {
            if (Number(fields[index]) && fields[index + 1] is { Length: 3 } currency && currency.All(char.IsAsciiLetterUpper) && Number(fields[index + 2]))
            {
                value = index;
                break;
            }
        }

        if (value < 0)
        {
            return null;
        }

        var aligned = new string?[labels.Length];
        aligned[0] = fields[0];
        aligned[1] = fields[1];
        aligned[2] = fields[2];
        aligned[3] = fields[3];
        aligned[4] = string.Join(',', fields[4..type]);
        aligned[5] = fields[type];
        aligned[6] = fields[type + 1];
        aligned[7] = fields[type + 2];
        aligned[8] = string.Join(',', fields[(type + 3)..cui]);
        aligned[9] = fields[cui];
        aligned[11] = string.Join(',', fields[(cui + 1)..Math.Max(cui + 1, announced - 1)]);
        aligned[12] = fields[announced - 1];
        aligned[13] = fields[announced];
        aligned[19] = fields[signed - 1];
        aligned[20] = fields[signed];
        aligned[21] = string.Join(',', fields[(signed + 1)..value]);
        for (var offset = 0; offset < 6 && 22 + offset < aligned.Length && value + offset < fields.Length; offset++)
        {
            aligned[22 + offset] = fields[value + offset];
        }

        return aligned;

        static bool Number(string? text) => decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _);

        static bool IsDate(string? text) => text is { Length: >= 10 } && System.Text.RegularExpressions.Regex.IsMatch(text, @"^\d{4}-\d{2}-\d{2}");
    }

    private static async IAsyncEnumerable<(int Row, string?[] Cells, bool Delimited)> CellsAsync(string path, FileFormat format, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var (row, cells, delimited) in RawCellsAsync(path, format, cancellationToken))
        {
            for (var index = 0; index < cells.Length; index++)
            {
                cells[index] = Clean(cells[index]);
            }

            yield return (row, cells, delimited);
        }
    }

    /// <summary>Drops unpaired UTF-16 surrogates and NUL characters, which some old workbooks contain and PostgreSQL refuses.</summary>
    public static string? Clean(string? text)
    {
        if (text is null || !text.Any(character => char.IsSurrogate(character) || character == '\0'))
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        for (var index = 0; index < text.Length; index++)
        {
            if (char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
            {
                builder.Append(text, index, 2);
                index++;
            }
            else if (!char.IsSurrogate(text[index]) && text[index] != '\0')
            {
                builder.Append(text[index]);
            }
        }

        return builder.ToString();
    }

    private static async IAsyncEnumerable<(int Row, string?[] Cells, bool Delimited)> RawCellsAsync(string path, FileFormat format, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var signature = new byte[8];
        await using (var probe = File.OpenRead(path))
        {
            _ = await probe.ReadAtLeastAsync(signature, signature.Length, throwOnEndOfStream: false, cancellationToken);
        }

        if (signature is [(byte)'P', (byte)'K', ..])
        {
            var rows = StreamingOds.IsOds(path) ? StreamingOds.ReadAsync(path, Columns, cancellationToken, allSheets: true) : StreamingXlsx.ReadAsync(path, Columns, cancellationToken, allSheets: true);
            await foreach (var (row, cells) in rows)
            {
                yield return (row, cells, false);
            }

            yield break;
        }

        if (signature is [0xD0, 0xCF, 0x11, 0xE0, ..])
        {
            foreach (var (row, cells) in LegacyXls.Read(path, Columns, allSheets: true))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return (row, cells, false);
            }

            yield break;
        }

        var (delimiter, quote) = await DelimiterAsync(path, cancellationToken);
        var counts = new DelimitedReader.MixedLineCounts();
        await using var stream = File.OpenRead(path);
        var number = 0;
        await foreach (var fields in DelimitedReader.ReadMixedAsync(stream, delimiter, quote, counts, cancellationToken))
        {
            format.Windows1250Lines = counts.Windows1250Lines;
            yield return (++number, fields, true);
        }
    }

    /// <summary>The header line decides: |…|,|…| is comma-delimited quoted with '|'; otherwise the most frequent of ^ | , ; tab.</summary>
    private static async Task<(char Delimiter, char Quote)> DelimiterAsync(string path, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(path, Encoding.Latin1);
        var line = await reader.ReadLineAsync(cancellationToken) ?? string.Empty;
        if (line.TrimStart('﻿', 'ï', '»', '¿').StartsWith('|') && line.Contains("|,|", StringComparison.Ordinal))
        {
            return (',', '|');
        }

        var delimiter = Delimiters.MaxBy(candidate => line.Count(character => character == candidate));
        return (delimiter, '"');
    }

    /// <summary>
    /// Dates as SEAP wrote them over the years: Excel serials, 2007-03-13 13:01:08.493, 02-04-2018 09:32:26,
    /// 09.03.2020 14:32:52, 7/1/2023 6:41:01 AM and 04/02/2018. Slash dates are month first unless a part over 12 says otherwise.
    /// </summary>
    public static DateOnly? Date(string? text, FileFormat format)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var token = text.Trim().Split(' ', 'T')[0];
        // An Excel serial: digits with at most one decimal point.
        if (token.All(character => char.IsAsciiDigit(character) || character == '.') && token.Count(character => character == '.') <= 1)
        {
            return StreamingXlsx.SerialDate(token);
        }

        if (DateOnly.TryParseExact(token, ["yyyy-MM-dd", "dd-MM-yyyy", "dd.MM.yyyy", "d.M.yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        var parts = token.Split('/');
        if (parts.Length == 3 && int.TryParse(parts[0], out var first) && int.TryParse(parts[1], out var second) && int.TryParse(parts[2], out var year) && year > 1990)
        {
            if (first > 12)
            {
                format.DayFirst = true;
            }
            else if (second > 12)
            {
                format.DayFirst = false;
            }

            var (day, month) = format.DayFirst == true || first > 12 ? (first, second) : (second, first);
            return month is >= 1 and <= 12 && day >= 1 && day <= DateTime.DaysInMonth(year, month) ? new DateOnly(year, month, day) : null;
        }

        return null;
    }
}
