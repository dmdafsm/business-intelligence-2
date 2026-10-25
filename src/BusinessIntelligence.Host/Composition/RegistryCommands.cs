using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using BusinessIntelligence.Modules.Registry;
using BusinessIntelligence.Modules.Registry.Onrc;
using BusinessIntelligence.Modules.Registry.Sources;
using Microsoft.EntityFrameworkCore;

namespace BusinessIntelligence.Host.Composition;

internal static class RegistryCommands
{
    private const string Usage = """
        Commands:
          registry import <catalog-id> <file>   import a pinned source file (CAEN, statuses, SIRUTA or OD_FIRME)
          registry status                       show published datasets and row counts
          registry show <cui|registry-number>   print one company from the published snapshot
        """;

    private static readonly JsonSerializerOptions Output = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static async Task<int> ExecuteAsync(string[] arguments, CancellationToken cancellationToken)
    {
        if (arguments is not (["registry", "import", _, _] or ["registry", "status"] or ["registry", "show", _]))
        {
            await Console.Error.WriteLineAsync(Usage);
            return 2;
        }

        var connection = await OperatorDatabase.ResolveConnectionAsync(cancellationToken);
        if (connection is null)
        {
            return 2;
        }

        await using var database = OperatorDatabase.Create(connection);
        try
        {
            switch (arguments)
            {
                case ["registry", "import", var id, var path]:
                    return await ImportAsync(database, SourceCatalog.Get(id), path, cancellationToken);
                case ["registry", "status"]:
                    await WriteAsync(await StatusAsync(database, cancellationToken), cancellationToken);
                    return 0;
                default:
                    return await ShowAsync(database, arguments[2], cancellationToken);
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or InvalidOperationException or FileNotFoundException)
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

    private static async Task<int> ImportAsync(
        Modules.Platform.Persistence.AppDbContext database, CatalogFile file, string path, CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        var reference = new ImportReferenceData(database, TimeProvider.System);
        var result = file.Kind switch
        {
            "onrc-caen" => await reference.CaenAsync(path, file, cancellationToken),
            "onrc-status" => await reference.StatusCodesAsync(path, file, cancellationToken),
            "siruta" => await reference.SirutaAsync(path, file, cancellationToken),
            ImportOnrcFirme.Kind => await new ImportOnrcFirme(database, TimeProvider.System).ExecuteAsync(path, file, cancellationToken),
            _ => throw new ArgumentException($"No importer for {file.Kind}."),
        };
        await WriteAsync(new
        {
            catalogId = file.Id,
            outcome = result.Outcome.ToString(),
            sourceFileId = result.SourceFileId,
            seconds = Math.Round(watch.Elapsed.TotalSeconds, 1),
            report = result.Report,
        }, cancellationToken);
        return 0;
    }

    private static async Task<object> StatusAsync(Modules.Platform.Persistence.AppDbContext database, CancellationToken cancellationToken)
    {
        var published = await database.PublishedDatasets.AsNoTracking()
            .Join(database.SourceFiles, item => item.SourceFileId, file => file.Id,
                (item, file) => new { item.Kind, file.CatalogId, file.ReferenceDate, item.PublishedAt, file.Id })
            .OrderBy(item => item.Kind)
            .ToListAsync(cancellationToken);
        var snapshot = published.FirstOrDefault(item => item.Kind == ImportOnrcFirme.Kind)?.Id;
        return new
        {
            published,
            caen = await database.Caen.CountAsync(cancellationToken),
            statusCodes = await database.StatusCodes.CountAsync(cancellationToken),
            siruta = await database.Siruta.CountAsync(cancellationToken),
            registrations = await database.Registrations.CountAsync(item => item.SnapshotId == snapshot, cancellationToken),
            companies = await database.Companies.CountAsync(item => item.SnapshotId == snapshot, cancellationToken),
        };
    }

    private static async Task<int> ShowAsync(Modules.Platform.Persistence.AppDbContext database, string identifier, CancellationToken cancellationToken)
    {
        var snapshot = await database.PublishedDatasets.AsNoTracking()
            .Where(item => item.Kind == ImportOnrcFirme.Kind).Select(item => (int?)item.SourceFileId).FirstOrDefaultAsync(cancellationToken);
        var (_, cui) = RomanianCui.Parse(identifier);
        var registration = cui is null ? identifier.Trim() : null;
        var company = await database.Companies.AsNoTracking()
            .Where(item => item.SnapshotId == snapshot && (cui != null ? item.Cui == cui : item.RegistrationNumber == registration))
            .FirstOrDefaultAsync(cancellationToken);
        if (company is null)
        {
            await Console.Error.WriteLineAsync("No company with that CUI or registry number in the published snapshot.");
            return 1;
        }

        var registrations = await database.Registrations.AsNoTracking()
            .Where(item => item.SnapshotId == snapshot && (company.Cui != null ? item.Cui == company.Cui : item.RegistrationNumber == company.RegistrationNumber))
            .OrderByDescending(item => item.RegistrationDate)
            .ToListAsync(cancellationToken);
        await WriteAsync(new { company, registrations }, cancellationToken);
        return 0;
    }

    private static async Task WriteAsync(object value, CancellationToken cancellationToken)
    {
        await using var stdout = Console.OpenStandardOutput();
        await JsonSerializer.SerializeAsync(stdout, value, Output, cancellationToken);
        await stdout.WriteAsync("\n"u8.ToArray(), cancellationToken);
    }
}
