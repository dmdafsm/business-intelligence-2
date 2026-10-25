using System.Buffers;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using BusinessIntelligence.Modules.Registry;

namespace BusinessIntelligence.Modules.Fiscal;

/// <summary>One taxpayer row of an MF identification release, cleaned but not yet checked against other releases.</summary>
public sealed record TaxpayerRow(
    long Cui, string? Name, string? Kind, string? UnitType, long? ParentCui, string? County, string? Locality, string? Sector,
    string? Street, string? StreetNumber, string? Block, string? Staircase, string? Floor, string? Apartment, string? AddressDetails,
    string? PostalCode, string? Phone, string? Fax, string? RegistrationNumber, string? AuthorizationAct, bool? VatPayer, DateOnly? VatSince,
    string? FiscalStatus, DateOnly? StatusDate, DateOnly? DeregistrationDate, DateOnly? LastDeclaration, DateOnly? LastProcessed,
    int[] Obligations, string? ObligationDates);

public sealed class IdentificationReadCounts
{
    public int Rows { get; set; }
    public int InvalidCui { get; set; }
    public int JoinedRecords { get; set; }
    public int RepairedSeparators { get; set; }
    public int MalformedRecords { get; set; }
    public int Windows1250Lines { get; set; }
    public int UndecodableLines { get; set; }
    public int PlaceholderPhones { get; set; }
    public int UnrecognisedFlags { get; set; }
    public int UnparsedDates { get; set; }
}

/// <summary>
/// Reads MF "date de identificare plătitori" files in their three layouts: quoted CSV without a header (2018, 2019),
/// quoted CSV with a lowercase header, itself quoted in 2021 (2020, 2021), and a '^' or '|' delimited file with a 61-column header (2022 on).
/// Lines are UTF-8, falling back to Windows-1250 line by line (2022 and 2025 are mostly Windows-1250).
/// </summary>
public static class MfIdentificationReader
{
    public const string ParserVersion = "mf-identification-v1";

    /// <summary>2018–2021 columns as named in the publisher's explicatii.csv; 2018 and 2019 files carry the first 39 or 40.</summary>
    internal static readonly string[] CsvColumns =
    [
        "cod_fiscal", "nume", "loc", "str", "nr", "di", "dp", "fax", "sect", "tel", "jud_com", "nr_com", "an_com", "act_aut", "tva",
        "sfarsit", "cp", "data_stare", "stare", "judet", "imp_100", "imp_120", "imp_200", "imp_410", "imp_416", "imp_420", "imp_423",
        "imp_430", "imp_439", "imp_500", "imp_602", "imp_701", "imp_710", "imp_755", "imp_756", "bilanturi", "imp_412", "imp_480",
        "imp_432", "detalii_adresa", "bloc", "scara", "etaj", "ap",
    ];

    internal static readonly string[] DelimitedColumns =
    [
        "COD_FISCAL", "DENUMIRE", "COD_FISCAL_PARINTE", "TIP_UNITATE", "TIP_CONTRIB", "LOCALITATE", "STRADA", "NR", "DATA_INREGISTRARE",
        "DATA_PRELUCRARE", "FAX", "SECTOR", "TELEFON", "JUDET_COMERT", "NR_COMERT", "AN_COMERT", "ACT_AUTORIZARE", "TVA", "DATA_RADIERE",
        "COD_POSTAL", "DATA_STARE", "STARE", "JUDET", "IMP100", "IMP120", "IMP130", "IMP160", "ACCIZE200", "CONT410", "CONT412", "CONT416",
        "CONT420", "CONT423", "CONT430", "CONT432", "IMP439", "IMP451", "IMP461", "IMP473", "IMP474", "CONT480", "IMP500", "IMP602",
        "IMP611", "IMP625", "IMP632", "IMP633", "IMP634", "IMP701", "IMP710", "IMP711", "IMP712", "IMP713", "IMP755", "IMP756", "IMP940",
        "DETALII_ADRESA", "BLOC", "SCARA", "ETAJ", "AP",
    ];

    /// <summary>Quoted records longer than this are a broken quote swallowing the following lines, not a real record.</summary>
    private const int MaximumRecordLines = 20;

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly Lazy<Encoding> Windows1250 = new(() =>
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1250, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    });

    private static readonly string[] Months =
        ["IANUARIE", "FEBRUARIE", "MARTIE", "APRILIE", "MAI", "IUNIE", "IULIE", "AUGUST", "SEPTEMBRIE", "OCTOMBRIE", "NOIEMBRIE", "DECEMBRIE"];

    // 2022, 2025, 2026: 17.07.2025 8:19:39; 2023: 14/Jul/23 8:19:39 AM; 2024: 14-Jul-2024 8:19:39 AM; before 2022: 01-01-2003.
    private static readonly string[] NumericDateFormats =
    [
        "dd.MM.yyyy", "dd.MM.yyyy H:mm:ss", "dd-MM-yyyy", "dd/MMM/yy", "dd/MMM/yy h:mm:ss tt", "dd-MMM-yyyy", "dd-MMM-yyyy h:mm:ss tt",
    ];

    public static async IAsyncEnumerable<TaxpayerRow> ReadAsync(
        Stream stream, IdentificationReadCounts counts, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var lines = new ByteLines(stream);
        var first = await lines.ReadAsync(cancellationToken);
        if (first is null)
        {
            yield break;
        }

        var header = Decode(first, counts).TrimStart('﻿');
        var delimiter = header.StartsWith("COD_FISCAL^", StringComparison.Ordinal) ? '^'
            : header.StartsWith("COD_FISCAL|", StringComparison.Ordinal) ? '|' : ',';
        if (delimiter != ',')
        {
            if (!header.Split(delimiter).Select(cell => cell.Trim()).SequenceEqual(DelimitedColumns))
            {
                throw new InvalidDataException("MF identification: unexpected header.");
            }

            await foreach (var fields in DelimitedRecordsAsync(lines, delimiter, counts, cancellationToken))
            {
                if (Map(new Columns(DelimitedColumns, delimited: true), fields, counts) is { } row)
                {
                    yield return row;
                }
            }

            yield break;
        }

        string[] names;
        string[]? firstRecord = null;
        var firstCells = ParseCsvLine(header, out _);
        if (firstCells[0].Trim() == "cod_fiscal")
        {
            names = [.. firstCells.Select(cell => cell.Trim())];
            if (!names.SequenceEqual(CsvColumns))
            {
                throw new InvalidDataException("MF identification: unexpected header.");
            }
        }
        else
        {
            firstRecord = await CompleteCsvRecordAsync(header, lines, counts, cancellationToken);
            if (firstRecord is null || firstRecord.Length is not (39 or 40))
            {
                throw new InvalidDataException("MF identification: a file without header must have 39 or 40 columns.");
            }

            names = CsvColumns[..firstRecord.Length];
        }

        var columns = new Columns(names, delimited: false);
        if (firstRecord is not null && Map(columns, firstRecord, counts) is { } firstRow)
        {
            yield return firstRow;
        }

        while (await lines.ReadAsync(cancellationToken) is { } raw)
        {
            var record = await CompleteCsvRecordAsync(Decode(raw, counts), lines, counts, cancellationToken);
            if (record is null)
            {
                continue;
            }

            if (record.Length != names.Length)
            {
                counts.MalformedRecords++;
                continue;
            }

            if (Map(columns, record, counts) is { } row)
            {
                yield return row;
            }
        }
    }

    /// <summary>
    /// Splits '^' or '|' lines into 61 fields. A trailing empty field is dropped; a record broken over two lines is joined;
    /// one separator too many is merged back into the street (or, when the type columns are shifted, the name).
    /// </summary>
    private static async IAsyncEnumerable<string[]> DelimitedRecordsAsync(
        ByteLines lines, char delimiter, IdentificationReadCounts counts, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var expected = DelimitedColumns.Length;
        string[]? held = null;
        var heldLines = 0;
        while (await lines.ReadAsync(cancellationToken) is { } raw)
        {
            var text = Decode(raw, counts);
            if (text.Length == 0)
            {
                continue;
            }

            var fields = text.Split(delimiter);
            if (held is not null)
            {
                if (Width(fields) >= expected)
                {
                    counts.MalformedRecords++;
                    held = null;
                }
                else
                {
                    fields = [.. held[..^1], held[^1] + fields[0], .. fields[1..]];
                    heldLines++;
                    held = null;
                    if (Width(fields) == expected)
                    {
                        counts.JoinedRecords++;
                    }
                }
            }
            else
            {
                heldLines = 1;
            }

            var width = Width(fields);
            if (width < expected)
            {
                if (heldLines < 3)
                {
                    held = fields;
                }
                else
                {
                    counts.MalformedRecords++;
                }

                continue;
            }

            if (width == expected + 1)
            {
                var at = IsKind(fields[4]) ? 6 : 1;
                fields = [.. fields[..at], fields[at] + delimiter + fields[at + 1], .. fields[(at + 2)..]];
                counts.RepairedSeparators++;
                width--;
            }

            if (width != expected)
            {
                counts.MalformedRecords++;
                continue;
            }

            yield return fields[..expected];
        }

        if (held is not null)
        {
            counts.MalformedRecords++;
        }

        static int Width(string[] fields) => fields.Length > 0 && fields[^1].Length == 0 ? fields.Length - 1 : fields.Length;

        static bool IsKind(string value) => value.Trim() is "PJ" or "PF" || value.StartsWith("ERR", StringComparison.Ordinal);
    }

    /// <summary>Parses a CSV record that starts on this line, reading further lines while a quoted value stays open.</summary>
    private static async Task<string[]?> CompleteCsvRecordAsync(string line, ByteLines lines, IdentificationReadCounts counts, CancellationToken cancellationToken)
    {
        var fields = ParseCsvLine(line, out var open);
        var used = 1;
        while (open)
        {
            if (used == MaximumRecordLines || await lines.ReadAsync(cancellationToken) is not { } next)
            {
                counts.MalformedRecords++;
                return null;
            }

            line += "\n" + Decode(next, counts);
            used++;
            fields = ParseCsvLine(line, out open);
        }

        return line.Length == 0 ? null : fields;
    }

    /// <summary>RFC 4180 with one leniency: a quote inside an unquoted value is kept as text.</summary>
    internal static string[] ParseCsvLine(string line, out bool open)
    {
        var fields = new List<string>();
        var value = new StringBuilder();
        var quoted = false;
        var atStart = true;
        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (quoted)
            {
                if (character != '"')
                {
                    value.Append(character);
                }
                else if (index + 1 < line.Length && line[index + 1] == '"')
                {
                    value.Append('"');
                    index++;
                }
                else
                {
                    quoted = false;
                }
            }
            else if (character == ',')
            {
                fields.Add(value.ToString());
                value.Clear();
                atStart = true;
                continue;
            }
            else if (character == '"' && atStart)
            {
                quoted = true;
            }
            else
            {
                value.Append(character);
            }

            atStart = false;
        }

        fields.Add(value.ToString());
        open = quoted;
        return [.. fields];
    }

    private static TaxpayerRow? Map(Columns columns, string[] fields, IdentificationReadCounts counts)
    {
        counts.Rows++;
        var (state, cui) = RomanianCui.Parse(fields[columns.Cui]);
        if (state != CuiState.Valid)
        {
            counts.InvalidCui++;
            return null;
        }

        string? Text(int? index) => index is { } at ? Clean(fields[at]) : null;
        DateOnly? Date(int? index) => ParseDate(Text(index), counts);

        var (vatPayer, vatSince) = Flag(Text(columns.Vat), counts);
        var obligations = new List<int>();
        Dictionary<string, DateOnly>? since = null;
        foreach (var (code, index) in columns.Obligations)
        {
            var (registered, date) = Flag(Text(index), counts);
            if (registered == true)
            {
                obligations.Add(code);
                if (date is { } known)
                {
                    (since ??= [])[code.ToString(CultureInfo.InvariantCulture)] = known;
                }
            }
        }

        obligations.Sort();
        var kind = Text(columns.Kind) is "PJ" or "PF" ? Text(columns.Kind) : null;
        var parent = Text(columns.Parent) is { } parentText && RomanianCui.Parse(parentText) is (CuiState.Valid, { } parentCui) ? parentCui : (long?)null;
        return new TaxpayerRow(
            cui!.Value, Text(columns.Name), kind, Text(columns.UnitType), parent, Text(columns.County), Text(columns.Locality),
            Text(columns.Sector) is { Length: 1 } sector && sector[0] is >= '1' and <= '6' ? sector : null,
            Text(columns.Street), Text(columns.Number), Text(columns.Block), Text(columns.Staircase), Text(columns.Floor),
            Text(columns.Apartment), Text(columns.Details), PostalCode(Text(columns.PostalCode)), Phone(Text(columns.Phone), counts),
            Phone(Text(columns.Fax), counts), RegistrationNumber(Text(columns.RegistryCounty), Text(columns.RegistryNumber), Text(columns.RegistryYear)),
            Text(columns.AuthorizationAct), vatPayer, vatSince, Text(columns.Status)?.ToUpperInvariant(), Date(columns.StatusDate),
            Date(columns.Deregistered), Date(columns.LastDeclaration), Date(columns.Processed), [.. obligations],
            since is null ? null : JsonSerializer.Serialize(since));
    }

    /// <summary>Trimmed text; blank and the publisher's placeholders ('-', 'null') are missing.</summary>
    internal static string? Clean(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length == 0 || trimmed == "-" || trimmed.Equals("null", StringComparison.OrdinalIgnoreCase) ? null : trimmed;
    }

    /// <summary>NU = not registered; DA = registered (2022 on); a date = registered since then (before 2022).</summary>
    internal static (bool? Registered, DateOnly? Since) Flag(string? value, IdentificationReadCounts counts)
    {
        switch (value?.ToUpperInvariant())
        {
            case null:
                return (null, null);
            case "NU":
                return (false, null);
            case "DA":
                return (true, null);
        }

        if (ParseDate(value, counts) is { } date)
        {
            return (true, date);
        }

        counts.UnrecognisedFlags++;
        return (null, null);
    }

    internal static DateOnly? ParseDate(string? value, IdentificationReadCounts counts)
    {
        if (value is null)
        {
            return null;
        }

        if (DateTime.TryParseExact(value, NumericDateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return DateOnly.FromDateTime(parsed);
        }

        // "03 Martie     2015": day, Romanian month name, year, padded with spaces.
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 3 && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var day)
            && Array.IndexOf(Months, parts[1].ToUpperInvariant()) is var month and >= 0
            && int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var year)
            && year is > 1800 and < 2100 && day >= 1 && day <= DateTime.DaysInMonth(year, month + 1))
        {
            return new DateOnly(year, month + 1, day);
        }

        counts.UnparsedDates++;
        return null;
    }

    /// <summary>Digits (and '+', '/', ',' between numbers); '0' and other numbers under six digits are placeholders.</summary>
    internal static string? Phone(string? value, IdentificationReadCounts counts)
    {
        if (value is null)
        {
            return null;
        }

        var builder = new StringBuilder(value.Length);
        var digits = 0;
        foreach (var character in value)
        {
            if (char.IsAsciiDigit(character))
            {
                builder.Append(character);
                digits++;
            }
            else if (character is '+' or '/' or ',' or ';')
            {
                builder.Append(character);
            }
        }

        var text = builder.ToString().Trim('/', ',', ';');
        if (digits < 6 || text.All(character => character is '0' or '/' or ',' or ';' or '+'))
        {
            counts.PlaceholderPhones++;
            return null;
        }

        return text;
    }

    /// <summary>Six digits; five-digit codes lost their leading zero (Bucharest) and get it back; anything else is missing.</summary>
    internal static string? PostalCode(string? value) =>
        value is null || !value.All(char.IsAsciiDigit) ? null
        : value.Length == 6 ? value
        : value.Length == 5 ? "0" + value
        : null;

    /// <summary>"J40" + "4576" + "1991" becomes "J40/4576/1991"; incomplete or malformed parts give no number.</summary>
    internal static string? RegistrationNumber(string? county, string? number, string? year)
    {
        if (county is null || number is null || year is null)
        {
            return null;
        }

        county = county.ToUpperInvariant();
        var validCounty = county.Length is 2 or 3 && county[0] is 'J' or 'F' or 'C' && county[1..].All(char.IsAsciiDigit);
        var validNumber = number.Length is >= 1 and <= 7 && number.All(char.IsAsciiDigit);
        var validYear = year.Length == 4 && year.All(char.IsAsciiDigit);
        number = number.TrimStart('0');
        return validCounty && validNumber && validYear && number.Length > 0 ? $"{county}/{number}/{year}" : null;
    }

    private static string Decode(byte[] line, IdentificationReadCounts counts)
    {
        try
        {
            return StrictUtf8.GetString(line);
        }
        catch (DecoderFallbackException)
        {
        }

        try
        {
            var text = Windows1250.Value.GetString(line);
            counts.Windows1250Lines++;
            return text;
        }
        catch (DecoderFallbackException)
        {
            counts.UndecodableLines++;
            return Encoding.UTF8.GetString(line);
        }
    }

    /// <summary>Column positions for one layout; null where the layout has no such column.</summary>
    private sealed class Columns
    {
        public Columns(string[] names, bool delimited)
        {
            int? At(string name) => Array.IndexOf(names, name) is var index and >= 0 ? index : null;
            Cui = 0;
            if (delimited)
            {
                (Name, Parent, UnitType, Kind, Locality, Street, Number) = (At("DENUMIRE"), At("COD_FISCAL_PARINTE"), At("TIP_UNITATE"),
                    At("TIP_CONTRIB"), At("LOCALITATE"), At("STRADA"), At("NR"));
                (LastDeclaration, Processed, Fax, Sector, Phone) = (At("DATA_INREGISTRARE"), At("DATA_PRELUCRARE"), At("FAX"), At("SECTOR"), At("TELEFON"));
                (RegistryCounty, RegistryNumber, RegistryYear, AuthorizationAct, Vat) = (At("JUDET_COMERT"), At("NR_COMERT"), At("AN_COMERT"),
                    At("ACT_AUTORIZARE"), At("TVA"));
                (Deregistered, PostalCode, StatusDate, Status, County) = (At("DATA_RADIERE"), At("COD_POSTAL"), At("DATA_STARE"), At("STARE"), At("JUDET"));
                (Details, Block, Staircase, Floor, Apartment) = (At("DETALII_ADRESA"), At("BLOC"), At("SCARA"), At("ETAJ"), At("AP"));
                Obligations = [.. names.Select((name, index) => (name, index))
                    .Where(item => item.name.StartsWith("IMP", StringComparison.Ordinal) || item.name.StartsWith("CONT", StringComparison.Ordinal)
                        || item.name.StartsWith("ACCIZE", StringComparison.Ordinal))
                    .Select(item => (Code(item.name), item.index))];
            }
            else
            {
                (Name, Locality, Street, Number) = (At("nume"), At("loc"), At("str"), At("nr"));
                (LastDeclaration, Processed, Fax, Sector, Phone) = (At("di"), At("dp"), At("fax"), At("sect"), At("tel"));
                (RegistryCounty, RegistryNumber, RegistryYear, AuthorizationAct, Vat) = (At("jud_com"), At("nr_com"), At("an_com"), At("act_aut"), At("tva"));
                (Deregistered, PostalCode, StatusDate, Status, County) = (At("sfarsit"), At("cp"), At("data_stare"), At("stare"), At("judet"));
                (Details, Block, Staircase, Floor, Apartment) = (At("detalii_adresa"), At("bloc"), At("scara"), At("etaj"), At("ap"));
                Obligations = [.. names.Select((name, index) => (name, index))
                    .Where(item => item.name.StartsWith("imp_", StringComparison.Ordinal))
                    .Select(item => (Code(item.name), item.index))];
            }

            static int Code(string name) => int.Parse(new string([.. name.Where(char.IsAsciiDigit)]), CultureInfo.InvariantCulture);
        }

        public int Cui { get; }
        public int? Name { get; }
        public int? Parent { get; }
        public int? UnitType { get; }
        public int? Kind { get; }
        public int? Locality { get; }
        public int? Street { get; }
        public int? Number { get; }
        public int? LastDeclaration { get; }
        public int? Processed { get; }
        public int? Fax { get; }
        public int? Sector { get; }
        public int? Phone { get; }
        public int? RegistryCounty { get; }
        public int? RegistryNumber { get; }
        public int? RegistryYear { get; }
        public int? AuthorizationAct { get; }
        public int? Vat { get; }
        public int? Deregistered { get; }
        public int? PostalCode { get; }
        public int? StatusDate { get; }
        public int? Status { get; }
        public int? County { get; }
        public int? Details { get; }
        public int? Block { get; }
        public int? Staircase { get; }
        public int? Floor { get; }
        public int? Apartment { get; }
        public (int Code, int Index)[] Obligations { get; }
    }

    /// <summary>Reads lines as bytes so each can be decoded on its own; "\r\n" and "\n" both end a line.</summary>
    private sealed class ByteLines(Stream stream)
    {
        private readonly byte[] _buffer = new byte[1 << 16];
        private readonly ArrayBufferWriter<byte> _line = new(1024);
        private int _position;
        private int _length;

        public async Task<byte[]?> ReadAsync(CancellationToken cancellationToken)
        {
            _line.ResetWrittenCount();
            while (true)
            {
                if (_position == _length)
                {
                    _length = await stream.ReadAsync(_buffer, cancellationToken);
                    _position = 0;
                    if (_length == 0)
                    {
                        return _line.WrittenCount == 0 ? null : Trimmed();
                    }
                }

                var found = _buffer.AsSpan(_position, _length - _position).IndexOf((byte)'\n');
                if (found < 0)
                {
                    _line.Write(_buffer.AsSpan(_position, _length - _position));
                    _position = _length;
                    continue;
                }

                _line.Write(_buffer.AsSpan(_position, found));
                _position += found + 1;
                return Trimmed();
            }
        }

        private byte[] Trimmed()
        {
            var bytes = _line.WrittenSpan;
            return (bytes.Length > 0 && bytes[^1] == '\r' ? bytes[..^1] : bytes).ToArray();
        }
    }
}
