using System.Text;

namespace BusinessIntelligence.Modules.Platform.Files;

/// <summary>Reads the records of a dBASE III table (a shapefile's .dbf) as name → text. Deleted records are skipped.</summary>
public static class Dbf
{
    public const int MaximumRecords = 5_000_000;

    public static IEnumerable<(int Row, Dictionary<string, string> Fields)> Read(Stream stream, Encoding encoding)
    {
        using var reader = new BinaryReader(stream, encoding, leaveOpen: true);
        var header = reader.ReadBytes(32);
        if (header.Length < 32)
        {
            throw new InvalidDataException("The .dbf header is truncated.");
        }

        var records = BitConverter.ToInt32(header, 4);
        var headerLength = BitConverter.ToInt16(header, 8);
        var recordLength = BitConverter.ToInt16(header, 10);
        if (records is < 0 or > MaximumRecords || recordLength <= 1)
        {
            throw new InvalidDataException("The .dbf header is not plausible.");
        }

        var fields = new List<(string Name, int Length)>();
        var read = 32;
        while (true)
        {
            var descriptor = reader.ReadBytes(1);
            read++;
            if (descriptor.Length == 0 || descriptor[0] == 0x0D)
            {
                break;
            }

            var rest = reader.ReadBytes(31);
            read += 31;
            var name = Encoding.ASCII.GetString([descriptor[0], .. rest[..10]]).TrimEnd('\0');
            fields.Add((name, rest[15]));
        }

        reader.ReadBytes(headerLength - read);
        for (var row = 1; row <= records; row++)
        {
            var record = reader.ReadBytes(recordLength);
            if (record.Length < recordLength)
            {
                yield break;
            }

            if (record[0] == '*')
            {
                continue;
            }

            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            var position = 1;
            foreach (var (name, length) in fields)
            {
                values[name] = encoding.GetString(record, position, length).Trim();
                position += length;
            }

            yield return (row, values);
        }
    }
}
