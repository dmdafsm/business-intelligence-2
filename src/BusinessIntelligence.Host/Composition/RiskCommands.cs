using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using BusinessIntelligence.Modules.Risk;
using Microsoft.EntityFrameworkCore;

namespace BusinessIntelligence.Host.Composition;

internal static class RiskCommands
{
    private const string Usage = """
        Commands:
          risk build    rebuild the warning signals from the registry, ANAF answers, financial statements and benchmarks
        """;

    private static readonly JsonSerializerOptions Output = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static async Task<int> ExecuteAsync(string[] arguments, CancellationToken cancellationToken)
    {
        if (arguments is not ["risk", "build"])
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
            await using var database = OperatorDatabase.Create(connection);
            var watch = Stopwatch.StartNew();
            var build = await new BuildRiskSignals(database, TimeProvider.System).ExecuteAsync(cancellationToken);
            await using var stdout = Console.OpenStandardOutput();
            await JsonSerializer.SerializeAsync(stdout, new { build.Id, build.FinancialYear, build.BenchmarkYear, build.Version, signals = await database.RiskSignals.GroupBy(item => item.Signal).Select(group => new { signal = group.Key, companies = group.Count() }).ToListAsync(cancellationToken), seconds = Math.Round(watch.Elapsed.TotalSeconds, 1) }, Output, cancellationToken);
            await stdout.WriteAsync("\n"u8.ToArray(), cancellationToken);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            await Console.Error.WriteLineAsync("Build refused: " + exception.Message);
            return 1;
        }
        catch (DbException exception)
        {
            await Console.Error.WriteLineAsync("Database operation failed: " + exception.Message);
            return 1;
        }
    }
}
