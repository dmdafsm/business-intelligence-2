using System.Buffers;
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
        await foreach (var record in ParseAsync(Lines(reader, cancellationToken), delimiter, '"', cancellationToken))
        {
            yield return record;
        }
    }

    /// <summary>
    /// The same over files whose lines mix encodings: each line is decoded as UTF-8 and, when that fails, as Windows-1250.
    /// <paramref name="quote"/> is the quoting character (some SEAP exports quote with '|').
    /// </summary>
    public static async IAsyncEnumerable<string[]> ReadMixedAsync(Stream stream, char delimiter, char quote, MixedLineCounts counts,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var record in ParseAsync(MixedLines(stream, counts, cancellationToken), delimiter, quote, cancellationToken))
        {
            yield return record;
        }
    }

    public sealed class MixedLineCounts
    {
        public int Windows1250Lines { get; set; }
    }

    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);
    private static readonly Lazy<Encoding> Windows1250 = new(() =>
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1250, EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback);
    });

    private static async IAsyncEnumerable<string> Lines(StreamReader reader, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            yield return line;
        }
    }

    private static async IAsyncEnumerable<string> MixedLines(Stream stream, MixedLineCounts counts, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var buffer = new byte[1 << 16];
        var line = new System.Buffers.ArrayBufferWriter<byte>();
        var first = true;
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            var start = 0;
            for (var index = 0; index < read; index++)
            {
                if (buffer[index] != (byte)'\n')
                {
                    continue;
                }

                line.Write(buffer.AsSpan(start, index - start));
                yield return Decode(line.WrittenSpan, ref first, counts);
                line.ResetWrittenCount();
                start = index + 1;
            }

            line.Write(buffer.AsSpan(start, read - start));
            if (line.WrittenCount > MaximumRecordCharacters * 4)
            {
                throw new InvalidDataException("A line is longer than the reader's limit.");
            }
        }

        if (line.WrittenCount > 0)
        {
            yield return Decode(line.WrittenSpan, ref first, counts);
        }
    }

    private static string Decode(ReadOnlySpan<byte> bytes, ref bool first, MixedLineCounts counts)
    {
        if (first && bytes.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            bytes = bytes[3..];
        }

        first = false;
        if (!bytes.IsEmpty && bytes[^1] == (byte)'\r')
        {
            bytes = bytes[..^1];
        }

        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            counts.Windows1250Lines++;
            return Windows1250.Value.GetString(bytes);
        }
    }

    private static async IAsyncEnumerable<string[]> ParseAsync(IAsyncEnumerable<string> lines, char delimiter, char quote,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var length = 0;
        await foreach (var line in lines.WithCancellation(cancellationToken))
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
                    if (character == quote && index + 1 < line.Length && line[index + 1] == quote)
                    {
                        field.Append(quote);
                        index++;
                    }
                    else if (character == quote)
                    {
                        quoted = false;
                    }
                    else
                    {
                        field.Append(character);
                    }
                }
                else if (character == quote && field.Length == 0)
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
