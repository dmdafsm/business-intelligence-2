using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using BusinessIntelligence.Modules.Platform.Persistence;
using BusinessIntelligence.Modules.Registry.Sources;
using Microsoft.EntityFrameworkCore;

namespace BusinessIntelligence.Modules.Jobs;

/// <summary>
/// Finds new files for a job's discovery rules on a CKAN portal, downloads them into the raw folder and pins them in
/// <c>jobs.catalog_files</c>. A file is new when its resource is not pinned and it is newer than every pinned file of its
/// kind and series. The files of one dataset are pinned together or not at all.
/// </summary>
public sealed class DiscoverFiles(AppDbContext database, HttpClient http, TimeProvider time)
{
    private static readonly Regex DatasetInUrl = new("/dataset/(?<id>[0-9a-f-]{36})/", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex ResourceInUrl = new("/resource/(?<id>[0-9a-f-]{36})/", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex Placeholder = new(@"\{(?<name>[a-zA-Z][a-zA-Z0-9]*)\}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public async Task<DiscoveryResult> ExecuteAsync(
        JobDefinitions definitions, JobDefinition job, string rawRoot, long? runId, bool dryRun, Action<string> log, CancellationToken cancellationToken)
    {
        var known = await database.DiscoveredFiles.AsNoTracking().ToListAsync(cancellationToken);
        SourceCatalog.Include(known.Select(file => file.ToCatalogFile()));
        var pinnedResources = new HashSet<string>(known.Select(file => file.ResourceId), StringComparer.OrdinalIgnoreCase);
        var pinnedDatasets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in SourceCatalog.All)
        {
            if (ResourceInUrl.Match(file.Url.AbsolutePath) is { Success: true } resource)
            {
                pinnedResources.Add(resource.Groups["id"].Value);
            }

            if (DatasetInUrl.Match(file.Url.AbsolutePath) is { Success: true } dataset)
            {
                pinnedDatasets.Add(dataset.Groups["id"].Value);
            }
        }

        var usedPaths = new HashSet<string>(SourceCatalog.All.Select(file => file.LocalPath).OfType<string>(), StringComparer.Ordinal);
        var pinned = new List<DiscoveredFile>();
        var candidates = new List<DiscoveredFile>();
        var errors = new List<string>();
        foreach (var rule in job.Discover)
        {
            var datasets = await SearchAsync(definitions.CkanApi, rule, cancellationToken);
            var olderFiles = 0;
            foreach (var dataset in datasets)
            {
                var found = Match(rule, dataset, pinnedResources, errors);
                if (found.Count == 0)
                {
                    continue;
                }

                if (rule.WholeDataset && pinnedDatasets.Contains(dataset.Id))
                {
                    log($"{dataset.Title}: {found.Count} new files in a dataset that is already pinned; left for the operator (the rule takes whole datasets).");
                    continue;
                }

                // Files older than the catalog are what the portal has always had: counted, not listed.
                var older = found.Count(item => NotNewer(item.File, item.Rule) is not null);
                olderFiles += older;
                if (older == found.Count)
                {
                    continue;
                }

                var accepted = new List<DiscoveredFile>();
                foreach (var (file, fileRule) in found)
                {
                    var reason = NotNewer(file, fileRule) ?? Refusal(definitions, file, fileRule, usedPaths, rawRoot)
                        ?? (candidates.Concat(accepted).Any(other => other.Id == file.Id || other.LocalPath == file.LocalPath) ? "another new file has this id or path" : null);
                    if (reason is not null)
                    {
                        log($"{file.Id}: skipped, {reason}.");
                        continue;
                    }

                    accepted.Add(file);
                }

                if (rule.WholeDataset && accepted.Count != found.Count)
                {
                    log($"{dataset.Title}: not every file can be taken, so none is.");
                    continue;
                }

                foreach (var file in accepted)
                {
                    file.JobId = job.Id;
                    file.RunId = runId;
                    log($"{file.Id}: new {file.Kind} file for {file.ReferenceDate:yyyy-MM-dd} ({FormatBytes(file.Bytes)} listed) at {file.Url}.");
                }

                candidates.AddRange(accepted);
                if (dryRun || accepted.Count == 0)
                {
                    continue;
                }

                try
                {
                    foreach (var file in accepted)
                    {
                        await DownloadAsync(definitions, file, rawRoot, log, cancellationToken);
                    }
                }
                catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidDataException or TaskCanceledException
                    && !cancellationToken.IsCancellationRequested)
                {
                    errors.Add($"{dataset.Title}: download failed, nothing of it pinned ({exception.Message}).");
                    log(errors[^1]);
                    continue;
                }

                var discoveredAt = time.GetUtcNow();
                foreach (var file in accepted)
                {
                    file.DiscoveredAt = discoveredAt;
                    usedPaths.Add(file.LocalPath);
                    pinnedResources.Add(file.ResourceId);
                }

                database.DiscoveredFiles.AddRange(accepted);
                await database.SaveChangesAsync(cancellationToken);
                pinned.AddRange(accepted);
                pinnedDatasets.Add(dataset.Id);
                SourceCatalog.Include(known.Concat(pinned).Select(file => file.ToCatalogFile()));
            }

            log($"{rule.Organization}: {datasets.Count} matching datasets checked; {olderFiles} files not newer than the catalog ignored.");
        }

        return new DiscoveryResult(dryRun ? candidates : pinned, errors);
    }

    private async Task<List<CkanDataset>> SearchAsync(Uri api, DiscoveryRule rule, CancellationToken cancellationToken)
    {
        var query = $"package_search?fq={Uri.EscapeDataString($"organization:{rule.Organization}")}&rows=1000&sort={Uri.EscapeDataString("metadata_created desc")}";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        using var response = await http.GetAsync(new Uri(api, query), timeout.Token);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token);
        if (!document.RootElement.TryGetProperty("success", out var success) || !success.GetBoolean())
        {
            throw new InvalidDataException("The CKAN search did not succeed.");
        }

        var datasets = new List<CkanDataset>();
        foreach (var item in document.RootElement.GetProperty("result").GetProperty("results").EnumerateArray())
        {
            var title = Text(item, "title") ?? string.Empty;
            if (rule.Dataset.Match(title) is not { Success: true } match)
            {
                continue;
            }

            var resources = item.TryGetProperty("resources", out var list) ? list.EnumerateArray().Select(resource => new CkanResource(
                Text(resource, "id") ?? string.Empty, Text(resource, "name") ?? string.Empty, Text(resource, "url") ?? string.Empty,
                Date(resource, "last_modified") ?? Date(resource, "created"),
                resource.TryGetProperty("size", out var size) && size.ValueKind == JsonValueKind.Number ? size.GetInt64() : null)).ToList() : [];
            datasets.Add(new CkanDataset(
                Text(item, "id") ?? string.Empty, title, Captures(rule.Dataset, match),
                item.TryGetProperty("organization", out var organization) ? Text(organization, "title") ?? rule.Organization : rule.Organization,
                Text(item, "license_title") ?? Text(item, "license_id") ?? "not stated", resources));
            if (datasets.Count == rule.MaxDatasets)
            {
                break;
            }
        }

        // Oldest first: each pinned release raises the bar for the next one, and histories apply in date order.
        datasets.Reverse();
        return datasets;
    }

    /// <summary>The dataset's unpinned resources that a file rule matches, as catalog entries (not yet downloaded).</summary>
    private static List<(DiscoveredFile File, FileRule Rule)> Match(DiscoveryRule rule, CkanDataset dataset, HashSet<string> pinnedResources, List<string> errors)
    {
        var matches = new List<(CkanResource Resource, FileRule Rule, Dictionary<string, string> Values)>();
        foreach (var resource in dataset.Resources)
        {
            var fileName = FileNameOf(resource.Url);
            foreach (var fileRule in rule.Files)
            {
                if (fileRule.Resource.Match(resource.Name.Trim()) is not { Success: true } nameMatch
                    || fileRule.File?.Match(fileName) is { Success: false })
                {
                    continue;
                }

                var values = new Dictionary<string, string>(dataset.Captures, StringComparer.Ordinal);
                foreach (var (key, value) in Captures(fileRule.Resource, nameMatch))
                {
                    values[key] = value;
                }

                if (fileRule.File?.Match(fileName) is { Success: true } fileMatch)
                {
                    foreach (var (key, value) in Captures(fileRule.File, fileMatch))
                    {
                        values[key] = value;
                    }
                }

                matches.Add((resource, fileRule, values));
                break;
            }
        }

        var datasetModified = matches.Select(item => item.Resource.Modified).Max();
        var files = new List<(DiscoveredFile, FileRule)>();
        foreach (var (resource, fileRule, values) in matches.Where(item => !pinnedResources.Contains(item.Resource.Id)))
        {
            foreach (var (name, map) in rule.Map)
            {
                if (values.TryGetValue(name, out var value) && map.TryGetValue(value, out var mapped))
                {
                    values[name] = mapped;
                }
            }

            var fileName = FileNameOf(resource.Url);
            values[DiscoveryNames.FileName] = fileName;
            values[DiscoveryNames.Slug] = Slug(Path.GetFileNameWithoutExtension(fileName));
            if (resource.Modified is { } modified)
            {
                values[DiscoveryNames.ModifiedName] = modified.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }

            if (datasetModified is { } latest)
            {
                values[DiscoveryNames.DatasetModifiedName] = latest.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }

            if (values.TryGetValue("year", out var year))
            {
                values["yearEnd"] = $"{year}-12-31";
                if (values.TryGetValue("quarter", out var quarter) && int.TryParse(quarter, CultureInfo.InvariantCulture, out var q) && q is >= 1 and <= 4)
                {
                    values["quarterEnd"] = $"{year}-{q * 3:00}-{DateTime.DaysInMonth(int.Parse(year, CultureInfo.InvariantCulture), q * 3):00}";
                }
            }

            try
            {
                var referenceDate = DateOnly.ParseExact(Render(fileRule.ReferenceDate, values), "yyyy-MM-dd", CultureInfo.InvariantCulture);
                files.Add((new DiscoveredFile
                {
                    Id = Render(fileRule.Id, values).ToLowerInvariant(),
                    Kind = fileRule.Kind,
                    Dataset = dataset.Title,
                    FileName = fileName,
                    Url = resource.Url,
                    Publisher = dataset.Publisher,
                    License = dataset.License,
                    ReferenceDate = referenceDate,
                    Bytes = resource.Size ?? 0,
                    Sha256 = string.Empty,
                    Year = fileRule.Year is null ? null : short.Parse(Render(fileRule.Year, values), CultureInfo.InvariantCulture),
                    Family = fileRule.Family is null ? null : Render(fileRule.Family, values),
                    LocalPath = Render(fileRule.Path, values),
                    Grouping = GroupOf(fileRule, fileName),
                    JobId = string.Empty,
                    ResourceId = resource.Id,
                    ResourceModified = resource.Modified,
                }, fileRule));
            }
            catch (Exception exception) when (exception is FormatException or OverflowException or KeyNotFoundException)
            {
                errors.Add($"{dataset.Title} / {resource.Name}: the rule for {fileRule.Kind} does not fit this file ({exception.Message}).");
            }
        }

        return files;
    }

    private static string? Refusal(JobDefinitions definitions, DiscoveredFile file, FileRule rule, HashSet<string> usedPaths, string rawRoot)
    {
        if (!Uri.TryCreate(file.Url, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps && !url.IsLoopback || !HostAllowed(definitions, url))
        {
            return "its URL is not on an allowed https host";
        }

        if (file.Bytes > definitions.MaxFileBytes)
        {
            return $"it is larger than the {FormatBytes(definitions.MaxFileBytes)} limit";
        }

        if (SafePath(rawRoot, file.LocalPath) is null || usedPaths.Contains(file.LocalPath))
        {
            return $"its path {file.LocalPath} is outside the raw folder or already used";
        }

        return SourceCatalog.All.Any(pinned => pinned.Id == file.Id) ? "a pinned file already has this id" : null;
    }

    /// <summary>
    /// Why a file is not newer than every pinned file of its kind and series, or null when it is. A series seen for the
    /// first time must be newer than the whole kind.
    /// </summary>
    private static string? NotNewer(DiscoveredFile file, FileRule rule)
    {
        var sameKind = SourceCatalog.OfKind(file.Kind).ToList();
        var series = sameKind.Where(pinned => GroupOf(rule, pinned.FileName) == file.Grouping).ToList();
        var newest = (series.Count > 0 ? series : sameKind).Select(pinned => (DateOnly?)pinned.ReferenceDate).Max();
        return file.ReferenceDate <= newest ? $"it is not newer than the pinned {newest:yyyy-MM-dd}" : null;
    }

    private async Task DownloadAsync(JobDefinitions definitions, DiscoveredFile file, string rawRoot, Action<string> log, CancellationToken cancellationToken)
    {
        var target = SafePath(rawRoot, file.LocalPath)!;
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (!File.Exists(target))
        {
            var partial = target + ".part";
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromHours(3));
            using var response = await http.GetAsync(new Uri(file.Url), HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();
            if (response.RequestMessage?.RequestUri is { } final && !HostAllowed(definitions, final))
            {
                throw new InvalidDataException($"The download of {file.Id} was redirected to {final.Host}, which is not allowed.");
            }

            await using (var source = await response.Content.ReadAsStreamAsync(timeout.Token))
            await using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
            {
                var buffer = new byte[1 << 16];
                long total = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, timeout.Token)) > 0)
                {
                    total += read;
                    if (total > definitions.MaxFileBytes)
                    {
                        throw new InvalidDataException($"{file.Id} is larger than the {FormatBytes(definitions.MaxFileBytes)} limit.");
                    }

                    await output.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
                }
            }

            File.Move(partial, target);
        }

        // An existing file is a complete earlier download (partial ones keep the .part name): it is hashed, not fetched again.
        await using var stream = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);
        file.Sha256 = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
        file.Bytes = stream.Length;
        log($"{file.Id}: {FormatBytes(file.Bytes)} at {file.LocalPath}, SHA-256 {file.Sha256}.");
    }

    /// <summary>The full path of a relative raw-folder path, or null when it would leave the folder.</summary>
    public static string? SafePath(string rawRoot, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Split('/', '\\').Any(part => part is ".." or "." or ""))
        {
            return null;
        }

        var root = Path.GetFullPath(rawRoot);
        var full = Path.GetFullPath(Path.Combine(root, relative));
        return full.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? full : null;
    }

    private static bool HostAllowed(JobDefinitions definitions, Uri url) =>
        definitions.AllowedHosts.Any(host => url.Host.Equals(host, StringComparison.OrdinalIgnoreCase)
            || url.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase));

    private static string GroupOf(FileRule rule, string fileName) =>
        rule.GroupFromFile?.Match(fileName) is { Success: true, Groups.Count: > 1 } match ? match.Groups[1].Value.ToLowerInvariant() : string.Empty;

    private static string Render(string template, Dictionary<string, string> values) =>
        Placeholder.Replace(template, match => values.TryGetValue(match.Groups["name"].Value, out var value)
            ? value
            : throw new KeyNotFoundException($"no value for {{{match.Groups["name"].Value}}}"));

    private static Dictionary<string, string> Captures(Regex pattern, Match match) =>
        pattern.GetGroupNames().Where(name => !int.TryParse(name, CultureInfo.InvariantCulture, out _) && match.Groups[name].Success)
            .ToDictionary(name => name, name => match.Groups[name].Value, StringComparer.Ordinal);

    private static string FileNameOf(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? Uri.UnescapeDataString(uri.Segments[^1]) : url;

    private static string Slug(string text) => Regex.Replace(text.ToLowerInvariant(), "[^a-z0-9]+", "-", RegexOptions.None, TimeSpan.FromSeconds(1)).Trim('-');

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static DateTimeOffset? Date(JsonElement element, string name) =>
        Text(element, name) is { } text && DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value)
            ? new DateTimeOffset(value, TimeSpan.Zero)
            : null;

    public static string FormatBytes(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.0} MB",
        _ => $"{bytes / 1024.0:0} KB",
    };

    private sealed record CkanDataset(string Id, string Title, Dictionary<string, string> Captures, string Publisher, string License, List<CkanResource> Resources);

    private sealed record CkanResource(string Id, string Name, string Url, DateTimeOffset? Modified, long? Size);
}

public sealed record DiscoveryResult(IReadOnlyList<DiscoveredFile> Files, IReadOnlyList<string> Errors);
