using System.Text.Json;

namespace BusinessIntelligence.Modules.Registry.Sources;

/// <summary>
/// A pinned source file: imports accept a local file only when its size and SHA-256 match this entry.
/// Year and Family identify financial statement files; LocalPath is their location under the raw-data folder.
/// </summary>
public sealed record CatalogFile(
    string Id, string Kind, string Dataset, string FileName, Uri Url, string Publisher, string License,
    DateOnly ReferenceDate, long Bytes, string Sha256, short? Year = null, string? Family = null, string? LocalPath = null);

/// <summary>
/// The embedded catalog, plus the files import jobs discovered later (<see cref="Include"/>, called by operator commands
/// at start).
/// </summary>
public static class SourceCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Lazy<IReadOnlyDictionary<string, CatalogFile>> Embedded = new(Load);
    private static IReadOnlyDictionary<string, CatalogFile>? _withDiscovered;

    private static IReadOnlyDictionary<string, CatalogFile> Files => Volatile.Read(ref _withDiscovered) ?? Embedded.Value;

    public static IEnumerable<CatalogFile> All => Files.Values;

    /// <summary>The kinds the embedded catalog has, which are the kinds an importer exists for.</summary>
    public static IReadOnlySet<string> EmbeddedKinds { get; } = new HashSet<string>(Embedded.Value.Values.Select(file => file.Kind), StringComparer.Ordinal);

    public static IEnumerable<CatalogFile> OfKind(string kind) => Files.Values.Where(file => file.Kind == kind);

    public static CatalogFile Get(string id) =>
        Files.TryGetValue(id, out var file) ? file : throw new ArgumentException($"Unknown source file '{id}'.", nameof(id));

    /// <summary>Adds discovered files to the embedded ones; an id that is already pinned to other content is refused.</summary>
    public static void Include(IEnumerable<CatalogFile> discovered)
    {
        var files = new Dictionary<string, CatalogFile>(Embedded.Value, StringComparer.Ordinal);
        foreach (var file in discovered)
        {
            if (files.TryGetValue(file.Id, out var pinned))
            {
                if (pinned.Sha256 != file.Sha256)
                {
                    throw new InvalidOperationException($"Discovered file {file.Id} differs from the pinned file with that id.");
                }

                continue;
            }

            files.Add(file.Id, file);
        }

        Volatile.Write(ref _withDiscovered, files);
    }

    private static Dictionary<string, CatalogFile> Load()
    {
        using var stream = typeof(SourceCatalog).Assembly.GetManifestResourceStream("BusinessIntelligence.Modules.Registry.Sources.catalog.json")
            ?? throw new InvalidOperationException("The source catalog resource is missing.");
        using var document = JsonDocument.Parse(stream);
        var files = document.RootElement.GetProperty("files").Deserialize<CatalogFile[]>(JsonOptions)
            ?? throw new InvalidOperationException("The source catalog is empty.");
        return files.ToDictionary(file => file.Id, StringComparer.Ordinal);
    }
}
