using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Xml;

namespace BusinessIntelligence.Modules.Platform.Files;

/// <summary>
/// Reads the first worksheet of a large .xlsx row by row. Shared strings are held in memory (one string per distinct
/// value); sheet rows are streamed. Cells come back as published text: numbers and date serials as their raw value.
/// Bounded: cells longer than <see cref="MaximumCellCharacters"/> and more than <see cref="MaximumRows"/> rows are refused.
/// </summary>
public static class StreamingXlsx
{
    public const int MaximumCellCharacters = 16_384;
    public const int MaximumRows = 5_000_000;
    public const int MaximumSharedStrings = 5_000_000;

    private static readonly XmlReaderSettings Settings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreWhitespace = false,
        Async = true,
    };

    /// <summary>
    /// Rows of <paramref name="columns"/> cells (missing cells are null), with their 1-based row number. With
    /// <paramref name="allSheets"/>, every sheet is read in order and row numbers continue from one sheet to the next
    /// (exports split a quarter into sheets of 65,000 rows).
    /// </summary>
    public static async IAsyncEnumerable<(int Row, string?[] Cells)> ReadAsync(
        string path, int columns, [EnumeratorCancellation] CancellationToken cancellationToken, bool allSheets = false)
    {
        using var archive = ZipFile.OpenRead(path);
        var shared = await SharedStringsAsync(archive, cancellationToken);
        var sheets = archive.Entries
            .Where(entry => entry.FullName.StartsWith("xl/worksheets/sheet", StringComparison.Ordinal) && entry.FullName.EndsWith(".xml", StringComparison.Ordinal))
            .OrderBy(entry => entry.FullName.Length).ThenBy(entry => entry.FullName, StringComparer.Ordinal)
            .ToList();
        if (sheets.Count == 0)
        {
            throw new InvalidDataException("The workbook has no worksheet.");
        }

        var offset = 0;
        foreach (var sheet in allSheets ? sheets : sheets.Take(1))
        {
            var last = 0;
            await foreach (var (row, cells) in SheetAsync(sheet, shared, columns, cancellationToken))
            {
                last = row;
                yield return (offset + row, cells);
            }

            offset += last;
        }
    }

    private static async IAsyncEnumerable<(int Row, string?[] Cells)> SheetAsync(
        ZipArchiveEntry sheet, List<string> shared, int columns, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var stream = sheet.Open();
        using var reader = XmlReader.Create(stream, Settings);
        var rows = 0;
        string?[]? cells = null;
        var rowNumber = 0;
        while (await reader.ReadAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "row")
            {
                rowNumber = int.TryParse(reader.GetAttribute("r"), out var number) ? number : rowNumber + 1;
                cells = new string?[columns];
                if (reader.IsEmptyElement)
                {
                    yield return (rowNumber, cells);
                    cells = null;
                }
            }
            else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "c" && cells is not null)
            {
                var column = ColumnIndex(reader.GetAttribute("r"));
                var type = reader.GetAttribute("t");
                var value = reader.IsEmptyElement ? null : await CellValueAsync(reader);
                if (value is not null && type == "s")
                {
                    var index = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
                    value = index >= 0 && index < shared.Count ? shared[index] : throw new InvalidDataException($"Shared string {index} is missing.");
                }

                if (value is { Length: > MaximumCellCharacters })
                {
                    throw new InvalidDataException($"Row {rowNumber}: a cell exceeds {MaximumCellCharacters} characters.");
                }

                if (column >= 0 && column < columns)
                {
                    cells[column] = value;
                }
            }
            else if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "row" && cells is not null)
            {
                if (++rows > MaximumRows)
                {
                    throw new InvalidDataException($"More than {MaximumRows} rows.");
                }

                yield return (rowNumber, cells);
                cells = null;
            }
        }
    }

    /// <summary>Excel's 1900 date system serial (fraction = time of day) as a date; null when it is not a plausible serial.</summary>
    public static DateOnly? SerialDate(string? value) =>
        double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var serial) && serial is > 1 and < 2958466
            ? DateOnly.FromDateTime(DateTime.FromOADate(serial))
            : null;

    private static async Task<string?> CellValueAsync(XmlReader reader)
    {
        // <c><v>…</v></c>, <c t="inlineStr"><is><t>…</t></is></c>, or rich text runs inside <is>.
        var depth = reader.Depth;
        string? value = null;
        while (await reader.ReadAsync() && !(reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth))
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName is "v" or "t" && !reader.IsEmptyElement)
            {
                value = (value ?? string.Empty) + await reader.ReadElementContentAsStringAsync();
                if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
                {
                    break;
                }
            }
        }

        return value;
    }

    private static async Task<List<string>> SharedStringsAsync(ZipArchive archive, CancellationToken cancellationToken)
    {
        var strings = new List<string>();
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
        {
            return strings;
        }

        await using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, Settings);
        System.Text.StringBuilder? current = null;
        while (await reader.ReadAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "si")
            {
                current = new System.Text.StringBuilder();
                if (reader.IsEmptyElement)
                {
                    strings.Add(string.Empty);
                    current = null;
                }
            }
            else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "t" && current is not null && !reader.IsEmptyElement)
            {
                current.Append(await reader.ReadElementContentAsStringAsync());
                if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "si")
                {
                    Add();
                }
            }
            else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "rPh")
            {
                // Phonetic hints are not part of the text.
                await reader.SkipAsync();
            }
            else if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "si" && current is not null)
            {
                Add();
            }
        }

        return strings;

        void Add()
        {
            if (strings.Count >= MaximumSharedStrings)
            {
                throw new InvalidDataException($"More than {MaximumSharedStrings} shared strings.");
            }

            strings.Add(current!.ToString());
            current = null;
        }
    }

    private static int ColumnIndex(string? reference)
    {
        if (string.IsNullOrEmpty(reference))
        {
            return -1;
        }

        var index = 0;
        foreach (var character in reference)
        {
            if (character is >= 'A' and <= 'Z')
            {
                index = index * 26 + (character - 'A' + 1);
            }
            else
            {
                break;
            }
        }

        return index - 1;
    }
}
