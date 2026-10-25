using System.Text.Json;

namespace BusinessIntelligence.Modules.Registry.Sources;

/// <summary>
/// A pinned source file: imports accept a local file only when its size and SHA-256 match this entry.
/// Year and Family identify financial statement files; LocalPath is their location under the raw-data folder.
/// </summary>
public sealed record CatalogFile(
    string Id, string Kind, string Dataset, string FileName, Uri Url, string Publisher, string License,
    DateOnly ReferenceDate, long Bytes, string Sha256, short? Year = null, string? Family = null, string? LocalPath = null);

public static class SourceCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Lazy<IReadOnlyDictionary<string, CatalogFile>> Files = new(Load);

    public static IEnumerable<CatalogFile> All => Files.Value.Values;

    public static IEnumerable<CatalogFile> OfKind(string kind) => Files.Value.Values.Where(file => file.Kind == kind);

    public static CatalogFile Get(string id) =>
        Files.Value.TryGetValue(id, out var file) ? file : throw new ArgumentException($"Unknown source file '{id}'.", nameof(id));

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
