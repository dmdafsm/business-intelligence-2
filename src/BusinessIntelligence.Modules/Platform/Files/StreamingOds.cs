using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml;

namespace BusinessIntelligence.Modules.Platform.Files;

/// <summary>
/// Reads the first table of an OpenDocument spreadsheet (.ods) row by row from content.xml. Numbers come back as their
/// office:value, dates as yyyy-MM-dd, other cells as their text. Repeated rows and cells are expanded, with a bound.
/// </summary>
public static class StreamingOds
{
    public const int MaximumRepeat = 1_000;

    public static bool IsOds(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        return archive.GetEntry("content.xml") is not null && archive.GetEntry("xl/workbook.xml") is null;
    }

    public static async IAsyncEnumerable<(int Row, string?[] Cells)> ReadAsync(string path, int columns, [EnumeratorCancellation] CancellationToken cancellationToken, bool allSheets = false)
    {
        using var archive = ZipFile.OpenRead(path);
        var entry = archive.GetEntry("content.xml") ?? throw new InvalidDataException("The .ods file has no content.xml.");
        await using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, Async = true });
        const string Table = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
        const string Office = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
        var row = 0;
        var tables = 0;
        while (await reader.ReadAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "table" && reader.NamespaceURI == Table && ++tables > 1 && !allSheets)
            {
                yield break;
            }

            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "table-row" || reader.NamespaceURI != Table)
            {
                continue;
            }

            var repeatRows = Math.Min(MaximumRepeat, int.TryParse(reader.GetAttribute("number-rows-repeated", Table), out var rows) ? rows : 1);
            var cells = new string?[columns];
            var column = 0;
            if (!reader.IsEmptyElement)
            {
                var depth = reader.Depth;
                while (await reader.ReadAsync() && !(reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth))
                {
                    if (reader.NodeType != XmlNodeType.Element || reader.LocalName is not ("table-cell" or "covered-table-cell"))
                    {
                        continue;
                    }

                    var repeat = Math.Min(MaximumRepeat, int.TryParse(reader.GetAttribute("number-columns-repeated", Table), out var count) ? count : 1);
                    var value = reader.GetAttribute("date-value", Office) is { Length: >= 10 } date ? date[..10] : reader.GetAttribute("value", Office);
                    if (!reader.IsEmptyElement)
                    {
                        var text = new StringBuilder();
                        var cellDepth = reader.Depth;
                        while (await reader.ReadAsync() && !(reader.NodeType == XmlNodeType.EndElement && reader.Depth == cellDepth))
                        {
                            if (reader.NodeType is XmlNodeType.Text or XmlNodeType.SignificantWhitespace)
                            {
                                text.Append(reader.Value);
                            }
                            else if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "p" && text.Length > 0)
                            {
                                text.Append('\n');
                            }
                        }

                        value ??= text.Length == 0 ? null : text.ToString().TrimEnd('\n');
                    }

                    for (var index = 0; index < repeat && column < columns; index++, column++)
                    {
                        cells[column] = value;
                    }
                }
            }

            // A long run of empty repeated rows marks the end of the data.
            if (repeatRows > 1 && cells.All(cell => cell is null))
            {
                row += repeatRows;
                continue;
            }

            for (var index = 0; index < repeatRows; index++)
            {
                yield return (++row, cells);
            }
        }
    }
}
