using System.Globalization;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace BusinessIntelligence.Modules.Registry.Geography;

/// <summary>
/// Reads the first worksheet of a small, plain XLSX file (shared and inline strings, numbers) as rows of text.
/// Enough for the SIRUTA workbook; it is not a general spreadsheet reader and rejects formulas.
/// </summary>
public static class SimpleXlsx
{
    private const long MaximumExpandedBytes = 64L * 1024 * 1024;
    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    public static IReadOnlyList<string[]> ReadFirstSheet(byte[] content)
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

        return rows;
    }

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
