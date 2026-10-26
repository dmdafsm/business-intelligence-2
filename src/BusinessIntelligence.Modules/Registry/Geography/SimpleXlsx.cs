using System.Globalization;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace BusinessIntelligence.Modules.Registry.Geography;

/// <summary>
/// Reads the first worksheet of a small, plain XLSX file (shared and inline strings, numbers) as rows of text.
/// Enough for the SIRUTA workbook and the INS CAEN correspondence table; it is not a general spreadsheet reader and
/// rejects formulas.
/// </summary>
public static class SimpleXlsx
{
    private const long MaximumExpandedBytes = 64L * 1024 * 1024;
    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    /// <param name="fillMergedCells">
    /// Place rows at their sheet row numbers (blank rows kept, as empty arrays) and give every cell of a merged range the
    /// value of its first cell, as the sheet shows it.
    /// </param>
    public static IReadOnlyList<string[]> ReadFirstSheet(byte[] content, bool fillMergedCells = false)
    {
        using var archive = new ZipArchive(new MemoryStream(content, false), ZipArchiveMode.Read);
        if (archive.Entries.Sum(entry => entry.Length) > MaximumExpandedBytes)
        {
            throw new InvalidDataException("Workbook expands beyond the size limit.");
        }

        var strings = new List<string>();
        if (archive.GetEntry("xl/sharedStrings.xml") is { } shared)
        {
            foreach (var item in Elements(shared, "si"))
            {
                // Rich text keeps runs in <r><t>; phonetic hints (<rPh>) are not part of the value.
                strings.Add(string.Concat(item.Elements(Main + "t").Concat(item.Elements(Main + "r").Elements(Main + "t")).Select(text => text.Value)));
            }
        }

        var sheet = archive.GetEntry("xl/worksheets/sheet1.xml") ?? throw new InvalidDataException("Workbook has no first worksheet.");
        var rows = new List<string[]>();
        foreach (var row in Elements(sheet, "row"))
        {
            if (fillMergedCells && int.TryParse((string?)row.Attribute("r"), NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            {
                while (rows.Count < number - 1)
                {
                    rows.Add([]);
                }
            }

            var values = new List<string>();
            foreach (var cell in row.Elements(Main + "c"))
            {
                if (cell.Element(Main + "f") is not null)
                {
                    throw new InvalidDataException("Formulas are not supported.");
                }

                var column = ColumnIndex((string?)cell.Attribute("r"));
                while (values.Count < column)
                {
                    values.Add(string.Empty);
                }

                var raw = cell.Element(Main + "v")?.Value ?? string.Concat(cell.Descendants(Main + "t").Select(text => text.Value));
                values.Add((string?)cell.Attribute("t") == "s" ? strings[int.Parse(raw, NumberStyles.None, CultureInfo.InvariantCulture)] : raw);
            }

            rows.Add([.. values]);
        }

        if (fillMergedCells)
        {
            foreach (var merge in Elements(sheet, "mergeCell"))
            {
                Fill(rows, (string?)merge.Attribute("ref") ?? string.Empty);
            }
        }

        return rows;
    }

    /// <summary>Copies the first cell of a range such as "C350:C353" into the other cells of the range.</summary>
    private static void Fill(List<string[]> rows, string range)
    {
        var parts = range.Split(':');
        if (parts.Length != 2)
        {
            return;
        }

        var (firstColumn, firstRow) = (ColumnIndex(parts[0]), RowIndex(parts[0]));
        var (lastColumn, lastRow) = (ColumnIndex(parts[1]), RowIndex(parts[1]));
        if (firstRow < 0 || firstRow >= rows.Count)
        {
            return;
        }

        var value = firstColumn < rows[firstRow].Length ? rows[firstRow][firstColumn] : string.Empty;
        for (var row = firstRow; row <= lastRow && row < rows.Count; row++)
        {
            if (rows[row].Length <= lastColumn)
            {
                var widened = new string[lastColumn + 1];
                Array.Fill(widened, string.Empty);
                rows[row].CopyTo(widened, 0);
                rows[row] = widened;
            }

            for (var column = firstColumn; column <= lastColumn; column++)
            {
                rows[row][column] = value;
            }
        }
    }

    private static int RowIndex(string reference) =>
        int.TryParse(new string([.. reference.SkipWhile(char.IsAsciiLetterUpper)]), NumberStyles.None, CultureInfo.InvariantCulture, out var row) ? row - 1 : -1;

    /// <summary>Streams the top-level repeated elements of a part without loading the whole part.</summary>
    private static IEnumerable<XElement> Elements(ZipArchiveEntry entry, string name)
    {
        using var reader = XmlReader.Create(entry.Open(), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
        reader.MoveToContent();
        while (!reader.EOF)
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == name)
            {
                yield return (XElement)XNode.ReadFrom(reader);
            }
            else
            {
                reader.Read();
            }
        }
    }

    private static int ColumnIndex(string? reference)
    {
        var index = 0;
        foreach (var character in reference ?? "A")
        {
            if (!char.IsAsciiLetterUpper(character))
            {
                break;
            }

            index = (index * 26) + (character - 'A' + 1);
        }

        return index - 1;
    }
}
