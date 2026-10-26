using System.Text.Json;
using System.Text.RegularExpressions;
using BusinessIntelligence.Modules.Registry.Sources;

namespace BusinessIntelligence.Modules.Jobs;

/// <summary>
/// The import jobs, read from <c>config/import-jobs.json</c>. Changing a schedule, a discovery rule or a command is a
/// configuration change; only a new kind of source file needs code (its importer).
/// </summary>
public sealed record JobDefinitions(
    string TimeZone, Uri CkanApi, IReadOnlyList<string> AllowedHosts, long MaxFileBytes, IReadOnlyList<JobDefinition> Jobs)
{
    /// <summary>Operator command groups a job may run. <c>database</c> and <c>jobs</c> are not among them.</summary>
    public static readonly IReadOnlySet<string> CommandGroups =
        new HashSet<string>(["registry", "financials", "fiscal", "procurement", "funds", "licences", "anaf", "search", "market"], StringComparer.Ordinal);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    private static readonly Regex Placeholder = new(@"\{(?<name>[a-z][a-zA-Z0-9-]*)(?::(?<argument>[^}]+))?\}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public TimeZoneInfo Zone => TimeZoneInfo.FindSystemTimeZoneById(TimeZone);

    public JobDefinition Get(string id) =>
        Jobs.FirstOrDefault(job => job.Id == id) ?? throw new ArgumentException($"There is no job '{id}' in the job file.", nameof(id));

    public static async Task<JobDefinitions> LoadAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Parse(await JsonSerializer.DeserializeAsync<JobFile>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("The job file is empty."));
    }

    public static JobDefinitions Parse(string json) =>
        Parse(JsonSerializer.Deserialize<JobFile>(json, JsonOptions) ?? throw new InvalidDataException("The job file is empty."));

    private static JobDefinitions Parse(JobFile file)
    {
        var problems = new List<string>();
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(file.TimeZone);
        }
        catch (TimeZoneNotFoundException)
        {
            problems.Add($"Unknown time zone '{file.TimeZone}'.");
        }

        if (!Uri.TryCreate(file.CkanApi ?? "https://data.gov.ro/api/3/action/", UriKind.Absolute, out var api) || api.Scheme != Uri.UriSchemeHttps
            && !api.IsLoopback)
        {
            problems.Add("ckanApi must be an https URL.");
        }

        if (file.AllowedHosts is not { Count: > 0 })
        {
            problems.Add("allowedHosts must name at least one host.");
        }

        var jobs = new List<JobDefinition>();
        foreach (var job in file.Jobs ?? [])
        {
            var where = $"Job '{job.Id}'";
            if (string.IsNullOrWhiteSpace(job.Id) || !Regex.IsMatch(job.Id, "^[a-z0-9][a-z0-9-]*$", RegexOptions.None, TimeSpan.FromSeconds(1)))
            {
                problems.Add($"{where}: the id must be lower-case letters, digits and dashes.");
                continue;
            }

            if (jobs.Any(item => item.Id == job.Id))
            {
                problems.Add($"{where} is defined twice.");
            }

            CronSchedule? schedule = null;
            if (job.Schedule is not null)
            {
                try
                {
                    schedule = CronSchedule.Parse(job.Schedule);
                }
                catch (FormatException exception)
                {
                    problems.Add($"{where}: {exception.Message}");
                }
            }

            var rules = new List<DiscoveryRule>();
            foreach (var (rule, index) in (job.Discover ?? []).Select((rule, index) => (rule, index + 1)))
            {
                if (ParseRule(rule, $"{where}, rule {index}", problems) is { } parsed)
                {
                    rules.Add(parsed);
                }
            }

            var commands = job.Commands ?? [];
            foreach (var command in commands)
            {
                CheckCommand(command, $"{where}, command '{string.Join(' ', command)}'", problems);
            }

            if (commands.Count == 0 && rules.Count == 0)
            {
                problems.Add($"{where} neither discovers files nor runs a command.");
            }

            jobs.Add(new JobDefinition(job.Id, job.Title ?? job.Id, schedule, rules, commands, TimeSpan.FromHours(job.TimeoutHours ?? 12)));
        }

        if (problems.Count > 0)
        {
            throw new InvalidDataException("The job file is not valid:\n- " + string.Join("\n- ", problems));
        }

        return new JobDefinitions(file.TimeZone, api!, file.AllowedHosts!, file.MaxFileBytes ?? 4L << 30, jobs);
    }

    private static DiscoveryRule? ParseRule(RuleEntry rule, string where, List<string> problems)
    {
        var dataset = Compile(rule.Dataset, $"{where}, dataset", problems);
        if (string.IsNullOrWhiteSpace(rule.Organization))
        {
            problems.Add($"{where}: organization is required.");
        }

        var files = new List<FileRule>();
        foreach (var file in rule.Files ?? [])
        {
            var at = $"{where}, kind '{file.Kind}'";
            var resource = Compile(file.Resource, $"{at}, resource", problems);
            var fileName = file.File is null ? null : Compile(file.File, $"{at}, file", problems);
            var grouping = file.GroupFromFile ?? rule.GroupFromFile;
            var group = grouping is null ? null : Compile(grouping, $"{at}, groupFromFile", problems);
            if (!SourceCatalog.EmbeddedKinds.Contains(file.Kind))
            {
                problems.Add($"{at}: no importer reads this kind.");
            }

            if (dataset is null || resource is null)
            {
                continue;
            }

            // A template may use the named groups of the patterns and the built-in names.
            var names = new HashSet<string>(DiscoveryNames.BuiltIn, StringComparer.Ordinal);
            names.UnionWith(dataset.GetGroupNames().Concat(resource.GetGroupNames()).Concat(fileName?.GetGroupNames() ?? []));
            var parsed = new FileRule(file.Kind, resource, fileName, file.Id, file.Path, file.ReferenceDate ?? rule.ReferenceDate ?? DiscoveryNames.Modified,
                file.Year ?? rule.Year, file.Family ?? rule.Family, group);
            foreach (var (name, template) in new[] { ("id", parsed.Id), ("path", parsed.Path), ("referenceDate", parsed.ReferenceDate), ("year", parsed.Year), ("family", parsed.Family) })
            {
                if (template is null)
                {
                    continue;
                }

                foreach (Match match in Placeholder.Matches(template))
                {
                    if (!names.Contains(match.Groups["name"].Value))
                    {
                        problems.Add($"{at}: {name} uses '{{{match.Groups["name"].Value}}}', which no pattern captures.");
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(parsed.Id) || string.IsNullOrWhiteSpace(parsed.Path))
            {
                problems.Add($"{at}: id and path are required.");
            }

            files.Add(parsed);
        }

        if (files.Count == 0)
        {
            problems.Add($"{where}: no file rules.");
        }

        var maps = (rule.Map ?? []).ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>(pair.Value, StringComparer.OrdinalIgnoreCase),
            StringComparer.Ordinal);
        return dataset is null || string.IsNullOrWhiteSpace(rule.Organization) ? null
            : new DiscoveryRule(rule.Organization, dataset, rule.MaxDatasets ?? 10, rule.WholeDataset ?? false, maps, files);
    }

    private static void CheckCommand(List<string> command, string where, List<string> problems)
    {
        if (command.Count == 0 || !CommandGroups.Contains(command[0]))
        {
            problems.Add($"{where}: a command starts with one of {string.Join(", ", CommandGroups.Order())}.");
            return;
        }

        foreach (var argument in command)
        {
            foreach (Match match in Placeholder.Matches(argument))
            {
                var name = match.Groups["name"].Value;
                var kind = match.Groups["argument"].Value;
                if (name == "raw" && kind.Length == 0)
                {
                    continue;
                }

                if (name is not ("latest-id" or "latest-path"))
                {
                    problems.Add($"{where}: unknown placeholder '{match.Value}'.");
                }
                else if (!SourceCatalog.EmbeddedKinds.Contains(kind))
                {
                    problems.Add($"{where}: '{match.Value}' names no known kind.");
                }
            }
        }
    }

    private static Regex? Compile(string? pattern, string where, List<string> problems)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            problems.Add($"{where} pattern is required.");
            return null;
        }

        try
        {
            return new Regex(pattern, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        }
        catch (ArgumentException exception)
        {
            problems.Add($"{where} pattern: {exception.Message}");
            return null;
        }
    }

    /// <summary>Replaces <c>{raw}</c>, <c>{latest-id:kind}</c> and <c>{latest-path:kind}</c> in a command.</summary>
    public static IReadOnlyList<string> Expand(IReadOnlyList<string> command, string rawRoot) =>
        [.. command.Select(argument => Placeholder.Replace(argument, match =>
        {
            if (match.Groups["name"].Value == "raw")
            {
                return rawRoot;
            }

            var kind = match.Groups["argument"].Value;
            var latest = SourceCatalog.OfKind(kind).OrderBy(file => file.ReferenceDate).ThenBy(file => file.Id, StringComparer.Ordinal).LastOrDefault()
                ?? throw new InvalidOperationException($"No pinned file of kind {kind}.");
            return match.Groups["name"].Value == "latest-id" ? latest.Id
                : Path.Combine(rawRoot, latest.LocalPath ?? throw new InvalidOperationException($"{latest.Id} has no local path."));
        }))];

    private sealed record JobFile(string TimeZone, string? CkanApi, List<string>? AllowedHosts, long? MaxFileBytes, List<JobEntry>? Jobs);

    private sealed record JobEntry(string Id, string? Title, string? Schedule, List<RuleEntry>? Discover, List<List<string>>? Commands, double? TimeoutHours);

    private sealed record RuleEntry(
        string Organization, string Dataset, int? MaxDatasets, bool? WholeDataset, string? ReferenceDate, string? Year, string? Family,
        string? GroupFromFile, Dictionary<string, Dictionary<string, string>>? Map, List<FileEntry>? Files);

    private sealed record FileEntry(
        string Kind, string Resource, string? File, string Id, string Path, string? ReferenceDate, string? Year, string? Family, string? GroupFromFile);
}

public sealed record JobDefinition(
    string Id, string Title, CronSchedule? Schedule, IReadOnlyList<DiscoveryRule> Discover, IReadOnlyList<IReadOnlyList<string>> Commands, TimeSpan Timeout);

/// <summary>
/// Datasets of one CKAN organization whose title matches <see cref="Dataset"/>, newest first. With
/// <see cref="WholeDataset"/>, files are taken only from a dataset none of whose files is pinned yet, so a release is
/// never pinned in part.
/// </summary>
public sealed record DiscoveryRule(
    string Organization, Regex Dataset, int MaxDatasets, bool WholeDataset, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Map,
    IReadOnlyList<FileRule> Files);

/// <summary>
/// One kind of file in a matching dataset: the resource name (and optionally the file name) must match. The templates
/// build the catalog entry. <see cref="GroupFromFile"/> splits one kind into series that are compared separately (for
/// example one list per programme); its first group, taken from the file name, names the series.
/// </summary>
public sealed record FileRule(
    string Kind, Regex Resource, Regex? File, string Id, string Path, string ReferenceDate, string? Year, string? Family, Regex? GroupFromFile);

public static class DiscoveryNames
{
    /// <summary>The URL's file name.</summary>
    public const string FileName = "file";

    /// <summary>The file name without extension, lower case, with dashes for anything but letters and digits.</summary>
    public const string Slug = "slug";

    /// <summary>The date the publisher last changed the resource.</summary>
    public const string ModifiedName = "modified";

    /// <summary>The newest change date among the dataset's matching resources: one date for a whole release.</summary>
    public const string DatasetModifiedName = "datasetModified";

    public const string Modified = "{modified}";

    public static readonly string[] BuiltIn = [FileName, Slug, ModifiedName, DatasetModifiedName, "quarterEnd", "yearEnd"];
}
