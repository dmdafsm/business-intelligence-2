using System.Globalization;
using System.Text;
using ExcelDataReader;

namespace BusinessIntelligence.Modules.Platform.Files;

/// <summary>
/// Reads the first sheet of an old binary .xls (BIFF) workbook with ExcelDataReader. Cells come back as text: numbers
/// in invariant culture, dates as yyyy-MM-dd. Only for small files: a sheet is read row by row but the format keeps
/// its string table in memory.
/// </summary>
public static class LegacyXls
{
    public const int MaximumRows = 2_000_000;

    static LegacyXls() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>Rows of the first sheet, or of every sheet with <paramref name="allSheets"/> (numbered continuously).</summary>
    public static IEnumerable<(int Row, string?[] Cells)> Read(string path, int columns, bool allSheets = false)
    {
        using var stream = File.OpenRead(path);
        using var reader = ExcelReaderFactory.CreateReader(stream);
        var row = 0;
        do
        {
            while (reader.Read())
            {
                if (++row > MaximumRows)
                {
                    throw new InvalidDataException($"More than {MaximumRows} rows.");
                }

                var cells = new string?[columns];
                for (var index = 0; index < Math.Min(columns, reader.FieldCount); index++)
                {
                    cells[index] = reader.GetValue(index) switch
                    {
                        null => null,
                        DateTime date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        double number => number.ToString("R", CultureInfo.InvariantCulture),
                        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                        var value => value.ToString(),
                    };
                }

                yield return (row, cells);
            }
        }
        while (allSheets && reader.NextResult());
    }
}

/// <summary>Rows of a workbook in either format, chosen by the file's signature: .xlsx streams, old .xls goes through <see cref="LegacyXls"/>.</summary>
public static class Spreadsheet
{
    public static async IAsyncEnumerable<(int Row, string?[] Cells)> ReadAsync(string path, int columns,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var signature = new byte[2];
        await using (var probe = File.OpenRead(path))
        {
            await probe.ReadExactlyAsync(signature, cancellationToken);
        }

        if (signature is [(byte)'P', (byte)'K'])
        {
            await foreach (var row in StreamingXlsx.ReadAsync(path, columns, cancellationToken))
            {
                yield return row;
            }

            yield break;
        }

        foreach (var row in LegacyXls.Read(path, columns))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return row;
        }
    }
}
