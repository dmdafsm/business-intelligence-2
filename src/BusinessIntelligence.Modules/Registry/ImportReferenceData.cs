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

    public const string CorrespondenceKind = "caen-correspondence";
    public const string CorrespondenceParserVersion = "ins-caen-correspondence-v2";

    public async Task<ImportResult> CaenCorrespondenceAsync(string path, CatalogFile file, CancellationToken cancellationToken)
    {
        var content = await VerifiedFileStream.ReadAllAsync(path, file, cancellationToken);
        var (pairs, defects) = ParseCaenCorrespondence(SimpleXlsx.ReadFirstSheet(content, fillMergedCells: true));
        return await ReplaceAsync(file, CorrespondenceParserVersion, database.CaenCorrespondence, pairs, new Dictionary<string, object>
        {
            ["pairs"] = pairs.Count,
            ["rev2Classes"] = pairs.Select(pair => pair.Rev2Code).Distinct().Count(),
            ["rev2Split"] = pairs.GroupBy(pair => pair.Rev2Code).Count(group => group.Count() > 1),
            ["rev3Classes"] = pairs.Select(pair => pair.Rev3Code).Distinct().Count(),
            ["intermediate"] = pairs.Count(pair => pair.Intermediate),
            ["sourceDefects"] = defects,
        }, cancellationToken);
    }

    /// <summary>
    /// Reads the INS table: after the header row ("Clasă CAEN Rev.2", name, "Clasă CAEN Rev.3", name), a row with a Rev.2
    /// class starts a group and every row with a Rev.3 class adds a pair to the current group; a trailing "*" marks an
    /// intermediate class. Merged cells count for every row they span: a Rev.3 class merged over several Rev.2 rows
    /// (aggregation) pairs with each of them. Colours that mark detailing, aggregation and recoding in the source are not
    /// read. A row whose
    /// Rev.2 cell is not a class and has no name (the September 2026 file ends with "0" next to 4689) is skipped and
    /// reported, and the rows after it join no group until the next Rev.2 class.
    /// </summary>
    public static (List<CaenCorrespondence> Pairs, List<string> Defects) ParseCaenCorrespondence(IReadOnlyList<string[]> rows)
    {
        var header = rows.Select((row, index) => (row, index))
            .FirstOrDefault(item => item.row.Length >= 4 && item.row[0].Trim().StartsWith("Clasă CAEN Rev.2", StringComparison.Ordinal)
                && item.row[2].Trim().StartsWith("Clasă CAEN Rev.3", StringComparison.Ordinal));
        if (header.row is null)
        {
            throw new InvalidDataException("CAEN correspondence: header row not found.");
        }

        var pairs = new List<CaenCorrespondence>();
        var defects = new List<string>();
        string? current = null;
        foreach (var (row, index) in rows.Select((row, index) => (row, index)).Skip(header.index + 1))
        {
            var rev2 = row.Length > 0 ? row[0].Trim() : string.Empty;
            var rev3 = row.Length > 2 ? row[2].Trim() : string.Empty;
            if (rev2.Length > 0 && !IsClass(rev2))
            {
                if (row.Length > 1 && row[1].Trim().Length > 0)
                {
                    throw new InvalidDataException($"CAEN correspondence: '{rev2}' is not a class code.");
                }

                defects.Add($"row {index + 1}: Rev.2 cell '{rev2}' without a name, next to '{rev3}', skipped");
                current = null;
                continue;
            }

            if (rev2.Length > 0)
            {
                current = rev2;
            }

            if (rev3.Length == 0)
            {
                continue;
            }

            var intermediate = rev3.EndsWith('*');
            rev3 = rev3.TrimEnd('*').Trim();
            if (!IsClass(rev3) || current is null)
            {
                throw new InvalidDataException($"CAEN correspondence: '{rev3}' is not a class code or has no Rev.2 class before it.");
            }

            if (!pairs.Any(pair => pair.Rev2Code == current && pair.Rev3Code == rev3))
            {
                pairs.Add(new CaenCorrespondence { Rev2Code = current, Rev3Code = rev3, Intermediate = intermediate });
            }
        }

        return pairs.Count > 0 ? (pairs, defects) : throw new InvalidDataException("CAEN correspondence: no pairs.");

        static bool IsClass(string code) => code.Length == 4 && code.All(char.IsAsciiDigit);
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
