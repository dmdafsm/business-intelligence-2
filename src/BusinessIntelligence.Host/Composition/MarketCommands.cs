using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using BusinessIntelligence.Modules.Market;

namespace BusinessIntelligence.Host.Composition;

internal static class MarketCommands
{
    private const string Usage = """
        Commands:
          market build    rebuild the statistics tables from the published registry snapshot and the registry history
        """;

    private static readonly JsonSerializerOptions Output = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static async Task<int> ExecuteAsync(string[] arguments, CancellationToken cancellationToken)
    {
        if (arguments is not ["market", "build"])
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
            var build = await new BuildMarketStatistics(database, TimeProvider.System).ExecuteAsync(cancellationToken);
            await using var stdout = Console.OpenStandardOutput();
            await JsonSerializer.SerializeAsync(stdout, new { build.Id, build.ReferenceDate, build.BuiltAt, seconds = Math.Round(watch.Elapsed.TotalSeconds, 1) }, Output, cancellationToken);
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
