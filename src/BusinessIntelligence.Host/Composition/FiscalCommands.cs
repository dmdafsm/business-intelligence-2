using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using BusinessIntelligence.Modules.Fiscal;
using BusinessIntelligence.Modules.Registry;
using Microsoft.EntityFrameworkCore;

namespace BusinessIntelligence.Host.Composition;

internal static class FiscalCommands
{
    private const string Usage = """
        Commands:
          fiscal import-all <raw-root> [--rebuild]  apply every pinned MF identification release, oldest first
          fiscal status                             releases applied and taxpayer periods
          fiscal show <cui>                         the periods of one taxpayer
        """;

    private static readonly JsonSerializerOptions Output = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static async Task<int> ExecuteAsync(string[] arguments, CancellationToken cancellationToken)
    {
        if (arguments is not (["fiscal", "import-all", _] or ["fiscal", "import-all", _, "--rebuild"] or ["fiscal", "status"] or ["fiscal", "show", _]))
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
            switch (arguments)
            {
                case ["fiscal", "status"]:
                    await using (var database = OperatorDatabase.Create(connection))
                    {
                        await WriteAsync(new
                        {
                            releases = await database.FiscalReleases.AsNoTracking().OrderBy(item => item.ReleaseDate).Select(item => item.ReleaseDate).ToListAsync(cancellationToken),
                            periods = await database.TaxpayerPeriods.CountAsync(cancellationToken),
                            taxpayers = await database.TaxpayerPeriods.CountAsync(item => item.Latest, cancellationToken),
                            kinds = await database.TaxpayerKinds.CountAsync(cancellationToken),
                        }, cancellationToken);
                    }

                    return 0;
                case ["fiscal", "show", var identifier]:
                    return await ShowAsync(connection, identifier, cancellationToken);
                default:
                    return await ImportAllAsync(connection, arguments[2], arguments.Length == 4, cancellationToken);
            }
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

    private static async Task<int> ImportAllAsync(string connection, string root, bool rebuild, CancellationToken cancellationToken)
    {
        var releases = ImportFiscalHistory.AvailableReleases();
        DateOnly? latest;
        List<DateOnly> applied;
        await using (var database = OperatorDatabase.Create(connection))
        {
            var import = new ImportFiscalHistory(database, TimeProvider.System);
            if (rebuild)
            {
                await import.ClearAsync(cancellationToken);
            }

            // Older releases need the taxpayer types of the newer ones to recognise individuals.
            applied = await database.FiscalReleases.AsNoTracking().Select(item => item.ReleaseDate).ToListAsync(cancellationToken);
            latest = applied.Count > 0 ? applied.Max() : null;
            if (releases.Any(release => release < ImportFiscalHistory.TypedReleases && (latest is null || release > latest)))
            {
                var watch = Stopwatch.StartNew();
                var kinds = await import.BuildKindsAsync(
                    releases.Where(release => release >= ImportFiscalHistory.TypedReleases).SelectMany(ImportFiscalHistory.Files), root, cancellationToken);
                await WriteAsync(new { taxpayerKinds = kinds, seconds = Math.Round(watch.Elapsed.TotalSeconds, 1) }, cancellationToken);
            }
        }

        foreach (var release in releases)
        {
            // A rerun skips what is applied; a release older than the history needs --rebuild, so it is reported, not refused.
            if (release <= latest)
            {
                await WriteAsync(new { release, outcome = applied.Contains(release) ? "AlreadyImported" : "SkippedOlderThanHistory" }, cancellationToken);
                continue;
            }

            // A fresh context per release keeps each transaction and change tracker separate.
            await using var database = OperatorDatabase.Create(connection);
            var watch = Stopwatch.StartNew();
            var result = await new ImportFiscalHistory(database, TimeProvider.System).ExecuteAsync(release, root, cancellationToken);
            await WriteAsync(new
            {
                release,
                outcome = result.Outcome.ToString(),
                seconds = Math.Round(watch.Elapsed.TotalSeconds, 1),
                report = result.Report,
            }, cancellationToken);

            // Each release rewrites most periods; vacuuming between releases lets the next one reuse that space.
            if (result.Outcome == ImportOutcome.Published)
            {
                await RegistryImports.VacuumAsync(database, "fiscal.taxpayer_periods", cancellationToken);
            }
        }

        return 0;
    }

    private static async Task<int> ShowAsync(string connection, string identifier, CancellationToken cancellationToken)
    {
        if (RomanianCui.Parse(identifier) is not (CuiState.Valid, { } cui))
        {
            await Console.Error.WriteLineAsync("Not a valid CUI.");
            return 2;
        }

        await using var database = OperatorDatabase.Create(connection);
        var periods = await database.TaxpayerPeriods.AsNoTracking().Where(item => item.Cui == cui).OrderBy(item => item.ValidFrom).ToListAsync(cancellationToken);
        if (periods.Count == 0)
        {
            await Console.Error.WriteLineAsync("No MF identification data for that CUI.");
            return 1;
        }

        await WriteAsync(periods, cancellationToken);
        return 0;
    }

    private static async Task WriteAsync(object value, CancellationToken cancellationToken)
    {
        await using var stdout = Console.OpenStandardOutput();
        await JsonSerializer.SerializeAsync(stdout, value, Output, cancellationToken);
        await stdout.WriteAsync("\n"u8.ToArray(), cancellationToken);
    }
}
