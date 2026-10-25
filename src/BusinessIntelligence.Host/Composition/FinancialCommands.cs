using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using BusinessIntelligence.Modules.Financials;
using BusinessIntelligence.Modules.Registry;
using BusinessIntelligence.Modules.Registry.Sources;
using Microsoft.EntityFrameworkCore;

namespace BusinessIntelligence.Host.Composition;

internal static class FinancialCommands
{
    private const string Usage = """
        Commands:
          financials import-all <raw-root>             import every pinned MF year and family found under <raw-root>
          financials import <year> <family> <raw-root>  import one year and family (BL, UU or IR)
          financials status                             statements per year and family
        """;

    private static readonly JsonSerializerOptions Output = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static async Task<int> ExecuteAsync(string[] arguments, CancellationToken cancellationToken)
    {
        if (arguments is not (["financials", "import-all", _] or ["financials", "import", _, _, _] or ["financials", "status"]))
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
            if (arguments is ["financials", "status"])
            {
                await using var database = OperatorDatabase.Create(connection);
                var rows = await database.FinancialStatements.AsNoTracking()
                    .GroupBy(item => new { item.Year, item.Family })
                    .Select(group => new { group.Key.Year, group.Key.Family, Statements = group.Count() })
                    .OrderBy(item => item.Year).ThenBy(item => item.Family)
                    .ToListAsync(cancellationToken);
                await WriteAsync(rows, cancellationToken);
                return 0;
            }

            var root = arguments[^1];
            var files = SourceCatalog.OfKind(ImportMfFinancials.DataKind)
                .Where(file => arguments[1] == "import-all"
                    || (file.Year?.ToString(System.Globalization.CultureInfo.InvariantCulture) == arguments[2]
                        && string.Equals(file.Family, arguments[3], StringComparison.OrdinalIgnoreCase)))
                .OrderBy(file => file.Year).ThenBy(file => file.Family)
                .ToList();
            if (files.Count == 0)
            {
                await Console.Error.WriteLineAsync("No pinned MF file matches.");
                return 1;
            }

            foreach (var data in files)
            {
                // One context per file keeps the change tracker small across 47 imports.
                await using var database = OperatorDatabase.Create(connection);
                var watch = Stopwatch.StartNew();
                var dictionary = SourceCatalog.Get(data.Id.Replace("-data", "-dictionary", StringComparison.Ordinal));
                var result = await new ImportMfFinancials(database, TimeProvider.System).ExecuteAsync(data, dictionary, root, cancellationToken);
                await WriteAsync(new
                {
                    catalogId = data.Id,
                    outcome = result.Outcome.ToString(),
                    seconds = Math.Round(watch.Elapsed.TotalSeconds, 1),
                    report = result.Report,
                }, cancellationToken);
            }

            await using (var database = OperatorDatabase.Create(connection))
            {
                await RegistryImports.VacuumAsync(database, "financials.statements", cancellationToken);
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
