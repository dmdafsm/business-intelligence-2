using BusinessIntelligence.Modules.Platform.Persistence;
using BusinessIntelligence.Modules.Registry.Sources;
using Microsoft.EntityFrameworkCore;

namespace BusinessIntelligence.Modules.Registry;

internal static class RegistryImports
{
    public static SourceFile NewSourceFile(CatalogFile file, string parserVersion, TimeProvider time, string report) => new()
    {
        CatalogId = file.Id,
        Kind = file.Kind,
        Dataset = file.Dataset,
        FileName = file.FileName,
        Url = file.Url.ToString(),
        Publisher = file.Publisher,
        License = file.License,
        ReferenceDate = file.ReferenceDate,
        Sha256 = file.Sha256,
        Bytes = file.Bytes,
        ParserVersion = parserVersion,
        ImportedAt = time.GetUtcNow(),
        Report = report,
    };

    /// <summary>
    /// Reclaims rows replaced by an import and refreshes planner statistics. Runs after the commit (VACUUM cannot run
    /// in a transaction); a failure here leaves the published data correct, only less compact.
    /// </summary>
    public static async Task VacuumAsync(AppDbContext database, string tables, CancellationToken cancellationToken)
    {
        database.Database.SetCommandTimeout(TimeSpan.FromHours(1));
#pragma warning disable EF1002 // Table names are constants from the importers, never input.
        await database.Database.ExecuteSqlRawAsync($"VACUUM (ANALYZE) {tables}", cancellationToken);
#pragma warning restore EF1002
    }

    /// <summary>Points the dataset at the new file inside the caller's transaction; returns the file it replaced.</summary>
    public static async Task<int?> PublishAsync(AppDbContext database, string kind, int sourceFileId, TimeProvider time, CancellationToken cancellationToken)
    {
        var published = await database.PublishedDatasets.FirstOrDefaultAsync(item => item.Kind == kind, cancellationToken);
        var previous = published?.SourceFileId;
        if (published is null)
        {
            database.PublishedDatasets.Add(new PublishedDataset { Kind = kind, SourceFileId = sourceFileId, PublishedAt = time.GetUtcNow() });
        }
        else
        {
            published.SourceFileId = sourceFileId;
            published.PublishedAt = time.GetUtcNow();
        }

        await database.SaveChangesAsync(cancellationToken);
        return previous;
    }
}
