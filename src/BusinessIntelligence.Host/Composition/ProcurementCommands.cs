using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using BusinessIntelligence.Modules.Procurement;
using Microsoft.EntityFrameworkCore;

namespace BusinessIntelligence.Host.Composition;

internal static class ProcurementCommands
{
    private const string Usage = """
        Commands:
          procurement import-all <raw-root>   import every pinned SEAP quarterly export (contracts, direct purchases)
          procurement status                  awards per kind, year and quarter
        """;

    private static readonly JsonSerializerOptions Output = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static async Task<int> ExecuteAsync(string[] arguments, CancellationToken cancellationToken)
    {
        if (arguments is not (["procurement", "import-all", _] or ["procurement", "status"]))
        {
            await Console.Error.WriteLineAsync(Usage);
            return 2;
        }

        var connection = await OperatorDatabase.ResolveConnectionAsync(cancellationToken);
        if (connection is null)
        {
            return 2;
        }

        try
        {
            if (arguments is ["procurement", "status"])
            {
                await using var database = OperatorDatabase.Create(connection);
                await WriteAsync(await database.ProcurementAwards.AsNoTracking()
                    .GroupBy(item => new { item.Kind, item.Year, item.Quarter })
                    .Select(group => new { group.Key.Kind, group.Key.Year, group.Key.Quarter, Rows = group.Count() })
                    .OrderBy(item => item.Kind).ThenBy(item => item.Year).ThenBy(item => item.Quarter)
                    .ToListAsync(cancellationToken), cancellationToken);
                return 0;
            }

            foreach (var file in ImportSeap.Files())
            {
                var path = Path.Combine(arguments[2], file.LocalPath ?? throw new InvalidOperationException($"{file.Id} has no local path."));
                var watch = Stopwatch.StartNew();
                await using var database = OperatorDatabase.Create(connection);
                var result = await new ImportSeap(database, TimeProvider.System).ExecuteAsync(path, file, cancellationToken);
                await WriteAsync(new { catalogId = file.Id, outcome = result.Outcome.ToString(), seconds = Math.Round(watch.Elapsed.TotalSeconds, 1), report = result.Report },
                    cancellationToken);
            }

            return 0;
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or InvalidOperationException or FileNotFoundException or DirectoryNotFoundException)
        {
            await Console.Error.WriteLineAsync("Import refused: " + exception.Message);
            return 1;
        }
        catch (DbException exception)
        {
            await Console.Error.WriteLineAsync("Database operation failed: " + exception.Message);
            return 1;
        }
    }

    private static async Task WriteAsync(object value, CancellationToken cancellationToken)
    {
        await using var stdout = Console.OpenStandardOutput();
        await JsonSerializer.SerializeAsync(stdout, value, Output, cancellationToken);
        await stdout.WriteAsync("\n"u8.ToArray(), cancellationToken);
    }
}
