using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using BusinessIntelligence.Modules.Anaf;
using Microsoft.EntityFrameworkCore;

namespace BusinessIntelligence.Host.Composition;

internal static class AnafCommands
{
    private const string Usage = """
        Commands:
          anaf harvest [--since yyyy-MM-dd] [--max-requests N]   ask ANAF about every CUI of the latest MF release (100 per request, 1 per second);
                                                                CUIs answered on or after --since (default: 7 days ago) are skipped, so a run resumes
          anaf status                                           answers stored per check date
        """;

    private static readonly JsonSerializerOptions Output = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static async Task<int> ExecuteAsync(string[] arguments, CancellationToken cancellationToken)
    {
        if (arguments.Length < 2 || arguments[1] is not ("harvest" or "status"))
        {
            await Console.Error.WriteLineAsync(Usage);
            return 2;
        }

        var options = arguments.Skip(2).Chunk(2).ToDictionary(pair => pair[0], pair => pair.Length > 1 ? pair[1] : string.Empty);
        var connection = await OperatorDatabase.ResolveConnectionAsync(cancellationToken);
        if (connection is null)
        {
            return 2;
        }

        try
        {
            await using var database = OperatorDatabase.Create(connection);
            if (arguments[1] == "status")
            {
                await WriteAsync(await database.AnafStatuses.AsNoTracking().GroupBy(item => item.CheckedOn)
                    .Select(group => new { checkedOn = group.Key, answers = group.Count(), found = group.Count(item => item.Found) })
                    .OrderBy(item => item.checkedOn).ToListAsync(cancellationToken), cancellationToken);
                return 0;
            }

            var since = options.TryGetValue("--since", out var sinceText)
                ? DateOnly.ParseExact(sinceText, "yyyy-MM-dd", CultureInfo.InvariantCulture)
                : DateOnly.FromDateTime(DateTime.Today.AddDays(-7));
            int? maximum = options.TryGetValue("--max-requests", out var maximumText) ? int.Parse(maximumText, CultureInfo.InvariantCulture) : null;
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("business-intelligence-2/1.0 (bulk status check, 1 request per second)");
            var result = await new HarvestAnafStatus(database, http, TimeProvider.System).ExecuteAsync(since, maximum,
                progress => Console.WriteLine(JsonSerializer.Serialize(progress)), cancellationToken);
            await WriteAsync(result, cancellationToken);
            return 0;
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException or ArgumentException)
        {
            await Console.Error.WriteLineAsync("Harvest stopped: " + exception.Message);
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
