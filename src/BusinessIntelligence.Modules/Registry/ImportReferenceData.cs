using System.Globalization;
using System.Text.Json;
using BusinessIntelligence.Modules.Platform.Persistence;
using BusinessIntelligence.Modules.Registry.Geography;
using BusinessIntelligence.Modules.Registry.Onrc;
using BusinessIntelligence.Modules.Registry.Sources;
using Microsoft.EntityFrameworkCore;

namespace BusinessIntelligence.Modules.Registry;

public enum ImportOutcome
{
    Published,

    /// <summary>The same file was already imported with the same parser; nothing changed.</summary>
    AlreadyImported,
}

public sealed record ImportResult(ImportOutcome Outcome, int SourceFileId, IReadOnlyDictionary<string, object> Report);

/// <summary>
/// Imports the small reference files (CAEN and status nomenclatures, SIRUTA). Each replaces its whole table in one
/// transaction, so readers see either the previous or the new version.
/// </summary>
public sealed class ImportReferenceData(AppDbContext database, TimeProvider time)
{
    public const string SirutaParserVersion = "siruta-xlsx-v1";
    private static readonly string[] SirutaHeader = ["SIRUTA", "DENLOC", "CODP", "JUD", "SIRSUP", "TIP", "NIV", "MED", "REGIUNE", "FSJ", "FSL"];

    public async Task<ImportResult> CaenAsync(string path, CatalogFile file, CancellationToken cancellationToken)
    {
        var content = await VerifiedFileStream.ReadAllAsync(path, file, cancellationToken);
        var (entries, defects) = OnrcNomenclatures.ParseCaen(content);
        return await ReplaceAsync(file, "onrc-nomenclature-v1", database.Caen, entries, new Dictionary<string, object>
        {
            ["entries"] = entries.Count,
            ["classes"] = entries.Count(entry => entry.Level == "class"),
            ["classesWithoutName"] = entries.Count(entry => entry.Level == "class" && entry.Name is null),
            ["versions"] = entries.Select(entry => entry.Version).Distinct().Order().ToArray(),
            ["sourceDefects"] = defects,
        }, cancellationToken);
    }

    public async Task<ImportResult> StatusCodesAsync(string path, CatalogFile file, CancellationToken cancellationToken)
    {
        var content = await VerifiedFileStream.ReadAllAsync(path, file, cancellationToken);
        var codes = OnrcNomenclatures.ParseStatusCodes(content);
        return await ReplaceAsync(file, "onrc-nomenclature-v1", database.StatusCodes, codes,
            new Dictionary<string, object> { ["codes"] = codes.Count }, cancellationToken);
    }

    public async Task<ImportResult> SirutaAsync(string path, CatalogFile file, CancellationToken cancellationToken)
    {
        var content = await VerifiedFileStream.ReadAllAsync(path, file, cancellationToken);
        var places = ParseSiruta(SimpleXlsx.ReadFirstSheet(content));
        return await ReplaceAsync(file, SirutaParserVersion, database.Siruta, places, new Dictionary<string, object>
        {
            ["places"] = places.Count,
            ["counties"] = places.Count(place => place.Level == 1),
            ["uats"] = places.Count(place => place.Level == 2),
            ["localities"] = places.Count(place => place.Level == 3),
        }, cancellationToken);
    }

    internal static List<SirutaPlace> ParseSiruta(IReadOnlyList<string[]> rows)
    {
        if (rows.Count < 2 || !rows[0].SequenceEqual(SirutaHeader))
        {
            throw new InvalidDataException("SIRUTA: unexpected header.");
        }

        var places = new List<SirutaPlace>(rows.Count - 1);
        foreach (var row in rows.Skip(1))
        {
            if (row.Length != SirutaHeader.Length)
            {
                throw new InvalidDataException($"SIRUTA: expected {SirutaHeader.Length} cells, found {row.Length}.");
            }

            places.Add(new SirutaPlace
            {
                Code = Number(row[0]),
                Name = row[1].Trim().Length > 0 ? row[1].Trim() : throw new InvalidDataException($"SIRUTA {row[0]}: blank name."),
                PostalCode = row[2].Trim() is "" or "0" ? null : row[2].Trim(),
                CountyNumber = (short)Number(row[3]),
                ParentCode = Number(row[4]),
                Type = (short)Number(row[5]),
                Level = (short)Number(row[6]),
                Environment = (short)Number(row[7]),
            });
        }

        var codes = places.Select(place => place.Code).ToHashSet();
        if (codes.Count != places.Count || places.Any(place => place.Level > 1 && !codes.Contains(place.ParentCode)))
        {
            throw new InvalidDataException("SIRUTA: duplicate codes or a parent that does not exist.");
        }

        return places;
    }

    private static int Number(string value) =>
        int.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            ? number
            : throw new InvalidDataException($"SIRUTA: '{value}' is not a whole number.");

    private async Task<ImportResult> ReplaceAsync<T>(
        CatalogFile file, string parserVersion, DbSet<T> table, IReadOnlyCollection<T> rows, Dictionary<string, object> report,
        CancellationToken cancellationToken) where T : class
    {
        var existing = await database.SourceFiles.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Sha256 == file.Sha256 && item.ParserVersion == parserVersion, cancellationToken);
        if (existing is not null)
        {
            return new ImportResult(ImportOutcome.AlreadyImported, existing.Id, report);
        }

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await table.ExecuteDeleteAsync(cancellationToken);
        var source = RegistryImports.NewSourceFile(file, parserVersion, time, JsonSerializer.Serialize(report));
        database.SourceFiles.Add(source);
        table.AddRange(rows);
        await database.SaveChangesAsync(cancellationToken);
        await RegistryImports.PublishAsync(database, file.Kind, source.Id, time, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        database.ChangeTracker.Clear();
        return new ImportResult(ImportOutcome.Published, source.Id, report);
    }
}
