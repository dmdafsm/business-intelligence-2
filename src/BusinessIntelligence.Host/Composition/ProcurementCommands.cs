using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using BusinessIntelligence.Modules.Funds;
using BusinessIntelligence.Modules.Licences;
using BusinessIntelligence.Modules.Procurement;
using BusinessIntelligence.Modules.Registry;
using Microsoft.EntityFrameworkCore;

namespace BusinessIntelligence.Host.Composition;

/// <summary>Import commands for the S08 open-data sources: each runs every pinned file of its kinds, oldest first.</summary>
internal static class ProcurementCommands
{
    private const string Usage = """
        Commands:
          procurement import-all <raw-root>   import every pinned SEAP quarterly export (contracts, direct purchases)
          procurement status                  awards per kind, year and quarter
          procurement summarize               rebuild the yearly totals per company (import-all does it too)
          funds import-all <raw-root>         import every pinned list of EU-funded projects (Ministry, AFIR)
          licences import-all <raw-root>      import every pinned regulator list (ANRSC, ANRM, CNAS)
        """;

    private static readonly JsonSerializerOptions Output = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static async Task<int> ExecuteAsync(string[] arguments, CancellationToken cancellationToken)
    {
        if (arguments is not (["procurement", "import-all", _] or ["procurement", "status"] or ["procurement", "summarize"] or ["funds", "import-all", _] or ["licences", "import-all", _]))
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

            if (arguments is ["procurement", "summarize"])
            {
                await using var summary = OperatorDatabase.Create(connection);
                await WriteAsync(new { yearTotals = await new SummarizeProcurement(summary).ExecuteAsync(cancellationToken) }, cancellationToken);
                return 0;
            }

            var files = arguments[0] switch { "funds" => ImportFunds.Files(), "licences" => ImportLicences.Files(), _ => ImportSeap.Files() };
            var published = false;
            foreach (var file in files)
            {
                var path = Path.Combine(arguments[2], file.LocalPath ?? throw new InvalidOperationException($"{file.Id} has no local path."));
                var watch = Stopwatch.StartNew();
                await using var database = OperatorDatabase.Create(connection);
                var result = arguments[0] switch
                {
                    "funds" => await new ImportFunds(database, TimeProvider.System).ExecuteAsync(path, file, cancellationToken),
                    "licences" => await new ImportLicences(database, TimeProvider.System).ExecuteAsync(path, file, cancellationToken),
                    _ => await new ImportSeap(database, TimeProvider.System).ExecuteAsync(path, file, cancellationToken),
                };
                await WriteAsync(new { catalogId = file.Id, outcome = result.Outcome.ToString(), seconds = Math.Round(watch.Elapsed.TotalSeconds, 1), report = result.Report },
                    cancellationToken);
                published |= result.Outcome == ImportOutcome.Published;
            }

            // The totals take minutes on the full history: rebuilt when a file changed, or when they were never built.
            await using var totalsDatabase = OperatorDatabase.Create(connection);
            if (arguments[0] == "procurement" && (published || !await totalsDatabase.ProcurementYearTotals.AnyAsync(cancellationToken)))
            {
                var watch = Stopwatch.StartNew();
                var totals = await new SummarizeProcurement(totalsDatabase).ExecuteAsync(cancellationToken);
                await WriteAsync(new { yearTotals = totals, seconds = Math.Round(watch.Elapsed.TotalSeconds, 1) }, cancellationToken);
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
