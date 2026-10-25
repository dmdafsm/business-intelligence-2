using System.IO.Compression;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using BusinessIntelligence.Modules.Registry.Sources;

namespace BusinessIntelligence.Tests.OpenData;

/// <summary>Synthetic source files for the S08 importers: workbooks, CSV, shapefile zips, with catalog entries.</summary>
internal static class OpenDataWorkbooks
{
    public static CatalogFile Catalog(string path, string id, string kind, DateOnly reference, string? fileName = null)
    {
        var bytes = File.ReadAllBytes(path);
        return new CatalogFile(id, kind, "Test dataset", fileName ?? Path.GetFileName(path), new Uri("https://example.test/" + id), "Test publisher", "CC BY 4.0",
            reference, bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    /// <summary>A one-sheet .xlsx with inline strings; numeric-looking cells are numbers.</summary>
    public static string Xlsx(string path, IEnumerable<IReadOnlyList<string?>> rows)
    {
        var sheet = new StringBuilder("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
        var number = 0;
        foreach (var row in rows)
        {
            number++;
            sheet.Append(System.Globalization.CultureInfo.InvariantCulture, $"<row r=\"{number}\">");
            for (var column = 0; column < row.Count; column++)
            {
                if (row[column] is not { } value)
                {
                    continue;
                }

                var reference = (column < 26 ? ((char)('A' + column)).ToString() : "A" + (char)('A' + column - 26)) + number;
                var cell = decimal.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _)
                    ? "<c r=\"" + reference + "\"><v>" + value + "</v></c>"
                    : "<c r=\"" + reference + "\" t=\"inlineStr\"><is><t>" + SecurityElement.Escape(value) + "</t></is></c>";
                sheet.Append(cell);
            }

            sheet.Append("</row>");
        }

        sheet.Append("</sheetData></worksheet>");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var stream = archive.CreateEntry("xl/worksheets/sheet1.xml").Open())
        {
            stream.Write(Encoding.UTF8.GetBytes(sheet.ToString()));
        }

        return path;
    }

    /// <summary>A shapefile zip with polygon records and a .dbf of text fields.</summary>
    public static string Shapefile(string path, IReadOnlyList<(double X, double Y)[]> polygons, IReadOnlyList<Dictionary<string, string>> records)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var stream = archive.CreateEntry("test.shp").Open())
        using (var writer = new BinaryWriter(stream))
        {
            var header = new byte[100];
            BitConverter.GetBytes(5).CopyTo(header, 32);
            writer.Write(header);
            var number = 0;
            foreach (var polygon in polygons)
            {
                var content = new MemoryStream();
                using (var record = new BinaryWriter(content, Encoding.UTF8, leaveOpen: true))
                {
                    record.Write(5);
                    for (var index = 0; index < 4; index++)
                    {
                        record.Write(0d);
                    }

                    record.Write(1);
                    record.Write(polygon.Length);
                    record.Write(0);
                    foreach (var (x, y) in polygon)
                    {
                        record.Write(x);
                        record.Write(y);
                    }
                }

                var bytes = content.ToArray();
                var recordHeader = new byte[8];
                System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(recordHeader, ++number);
                System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(recordHeader.AsSpan(4), bytes.Length / 2);
                writer.Write(recordHeader);
                writer.Write(bytes);
            }
        }

        using (var stream = archive.CreateEntry("test.dbf").Open())
        {
            stream.Write(Dbf(records));
        }

        return path;
    }

    private static byte[] Dbf(IReadOnlyList<Dictionary<string, string>> records)
    {
        var names = records[0].Keys.ToList();
        const int Length = 60;
        var output = new MemoryStream();
        var header = new byte[32];
        header[0] = 3;
        BitConverter.GetBytes(records.Count).CopyTo(header, 4);
        BitConverter.GetBytes((short)(32 + names.Count * 32 + 1)).CopyTo(header, 8);
        BitConverter.GetBytes((short)(1 + names.Count * Length)).CopyTo(header, 10);
        output.Write(header);
        foreach (var name in names)
        {
            var descriptor = new byte[32];
            Encoding.ASCII.GetBytes(name).CopyTo(descriptor, 0);
            descriptor[11] = (byte)'C';
            descriptor[16] = Length;
            output.Write(descriptor);
        }

        output.WriteByte(0x0D);
        foreach (var record in records)
        {
            output.WriteByte((byte)' ');
            foreach (var name in names)
            {
                var value = Encoding.UTF8.GetBytes(record[name]);
                var field = Enumerable.Repeat((byte)' ', Length).ToArray();
                value.AsSpan(0, Math.Min(value.Length, Length)).CopyTo(field);
                output.Write(field);
            }
        }

        return output.ToArray();
    }
}
