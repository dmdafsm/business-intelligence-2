using System.Runtime.CompilerServices;
using System.Text;

namespace BusinessIntelligence.Modules.Platform.Files;

/// <summary>
/// Reads RFC 4180-style delimited text: quoted fields may hold the delimiter, doubled quotes and line breaks. Bounded:
/// a record longer than <see cref="MaximumRecordCharacters"/> is refused.
/// </summary>
public static class DelimitedReader
{
    public const int MaximumRecordCharacters = 1 << 20;

    public static async IAsyncEnumerable<string[]> ReadAsync(Stream stream, char delimiter, Encoding encoding,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true, 1 << 16, leaveOpen: true);
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var length = 0;
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (!quoted && line.Length == 0 && fields.Count == 0)
            {
                continue;
            }

            length += line.Length;
            if (length > MaximumRecordCharacters)
            {
                throw new InvalidDataException($"A record is longer than {MaximumRecordCharacters} characters.");
            }

            for (var index = 0; index < line.Length; index++)
            {
                var character = line[index];
                if (quoted)
                {
                    if (character == '"' && index + 1 < line.Length && line[index + 1] == '"')
                    {
                        field.Append('"');
                        index++;
                    }
                    else if (character == '"')
                    {
                        quoted = false;
                    }
                    else
                    {
                        field.Append(character);
                    }
                }
                else if (character == '"' && field.Length == 0)
                {
                    quoted = true;
                }
                else if (character == delimiter)
                {
                    fields.Add(field.ToString());
                    field.Clear();
                }
                else
                {
                    field.Append(character);
                }
            }

            if (quoted)
            {
                // The quoted field continues on the next line.
                field.Append('\n');
                continue;
            }

            fields.Add(field.ToString());
            field.Clear();
            yield return [.. fields];
            fields.Clear();
            length = 0;
        }

        if (quoted)
        {
            throw new InvalidDataException("The file ends inside a quoted field.");
        }
    }
}
