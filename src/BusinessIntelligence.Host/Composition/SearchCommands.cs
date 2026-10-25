using System.Data.Common;
using System.Text.Json;
using BusinessIntelligence.Modules.Registry;
using BusinessIntelligence.Modules.Search;
using Microsoft.EntityFrameworkCore;

namespace BusinessIntelligence.Host.Composition;

internal static class SearchCommands
{
    private const string Usage = """
        Commands:
          search build    rebuild the search and grouping tables from the published registry snapshot
          search status   the current build and its report
        """;

    private static readonly JsonSerializerOptions Output = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static async Task<int> ExecuteAsync(string[] arguments, CancellationToken cancellationToken)
    {
        if (arguments is not (["search", "build"] or ["search", "status"]))
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
            if (arguments is ["search", "status"])
            {
                var current = await database.SearchBuilds.AsNoTracking().OrderByDescending(item => item.Id).FirstOrDefaultAsync(cancellationToken);
                await WriteAsync(current is null ? new { built = false } : Describe(current), cancellationToken);
                return 0;
            }

            var build = await new BuildSearchIndex(database, TimeProvider.System).ExecuteAsync(cancellationToken);
            await RegistryImports.VacuumAsync(database, BuildSearchIndex.Tables, cancellationToken);
            await WriteAsync(Describe(build), cancellationToken);
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

    private static object Describe(SearchBuild build) => new
    {
        built = true,
        build.Id,
        build.RegistrySnapshotId,
        build.BuiltAt,
        report = JsonDocument.Parse(build.Report).RootElement,
    };

    private static async Task WriteAsync(object value, CancellationToken cancellationToken)
    {
        await using var stdout = Console.OpenStandardOutput();
        await JsonSerializer.SerializeAsync(stdout, value, Output, cancellationToken);
        await stdout.WriteAsync("\n"u8.ToArray(), cancellationToken);
    }
}
