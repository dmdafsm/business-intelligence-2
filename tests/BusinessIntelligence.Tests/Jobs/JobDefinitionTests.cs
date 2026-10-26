using BusinessIntelligence.Modules.Jobs;
using Xunit;

namespace BusinessIntelligence.Tests.Jobs;

public sealed class JobDefinitionTests
{
    private static readonly TimeZoneInfo Bucharest = TimeZoneInfo.FindSystemTimeZoneById("Europe/Bucharest");

    [Theory]
    // Daily at 07:30 local time, from a summer afternoon: the next morning (UTC+3).
    [InlineData("30 7 * * *", "2026-07-10T14:00:00+03:00", "2026-07-11T07:30:00+03:00")]
    // Mondays at 08:00; 2026-10-26 is a Monday, so from 08:00 the next one is a week later (UTC+2 by then).
    [InlineData("0 8 * * 1", "2026-10-26T08:00:00+02:00", "2026-11-02T08:00:00+02:00")]
    // The 3rd of each month at 01:00.
    [InlineData("0 1 3 * *", "2026-12-05T00:00:00+02:00", "2027-01-03T01:00:00+02:00")]
    // Every 15 minutes.
    [InlineData("*/15 * * * *", "2026-10-26T10:07:00+02:00", "2026-10-26T10:15:00+02:00")]
    // Both day fields restricted: the 30th or a Sunday (written as 7), whichever comes first; the 30th is a Friday.
    [InlineData("0 6 30 * 7", "2026-10-26T10:00:00+02:00", "2026-10-30T06:00:00+02:00")]
    [InlineData("0 6 * * 7", "2026-10-26T10:00:00+02:00", "2026-11-01T06:00:00+02:00")]
    [InlineData("0 9-17/4 * * 1-5", "2026-10-30T18:00:00+02:00", "2026-11-02T09:00:00+02:00")]
    public void CronGivesTheNextLocalTime(string expression, string after, string expected)
    {
        var next = CronSchedule.Parse(expression).Next(DateTimeOffset.Parse(after, System.Globalization.CultureInfo.InvariantCulture), Bucharest);
        Assert.Equal(DateTimeOffset.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), next);
    }

    [Theory]
    [InlineData("* * * *")]
    [InlineData("60 * * * *")]
    [InlineData("0 24 * * *")]
    [InlineData("0 0 0 * *")]
    [InlineData("*/0 * * * *")]
    [InlineData("5-1 * * * *")]
    [InlineData("a * * * *")]
    public void InvalidCronIsRefused(string expression) => Assert.Throws<FormatException>(() => CronSchedule.Parse(expression));

    [Fact]
    public async Task TheRepositoryJobFileIsValid()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "BusinessIntelligence.slnx")))
        {
            root = root.Parent!;
        }

        var definitions = await JobDefinitions.LoadAsync(Path.Combine(root.FullName, "config", "import-jobs.json"), CancellationToken.None);
        Assert.Contains(definitions.Jobs, job => job.Id == "onrc" && job.Discover.Count == 2 && job.Schedule is not null);
        Assert.All(definitions.Jobs, job => Assert.NotNull(job.Schedule));
        Assert.Equal(["data.gov.ro"], definitions.AllowedHosts);
    }

    [Fact]
    public void AnInvalidFileListsEveryProblem()
    {
        var error = Assert.Throws<InvalidDataException>(() => JobDefinitions.Parse("""
            {
              "timeZone": "Europe/Bucharest",
              "allowedHosts": ["data.gov.ro"],
              "jobs": [
                { "id": "a", "schedule": "0 25 * * *", "commands": [["database", "migrate"]] },
                { "id": "a", "commands": [["registry", "import", "{latest-id:no-such-kind}", "{nope}"]] },
                {
                  "id": "b",
                  "discover": [{
                    "organization": "onrc",
                    "dataset": "^Firme (?<year>\\d{4})$",
                    "files": [
                      { "kind": "unknown-kind", "resource": "^X$", "id": "x-{year}-{month}", "path": "x/{file}" },
                      { "kind": "onrc-firme", "resource": "(", "id": "y", "path": "y" }
                    ]
                  }]
                },
                { "id": "Bad Id", "commands": [["search", "build"]] }
              ]
            }
            """));
        Assert.Contains("Job 'a': '25' is not a valid field", error.Message, StringComparison.Ordinal);
        Assert.Contains("a command starts with one of", error.Message, StringComparison.Ordinal);
        Assert.Contains("Job 'a' is defined twice.", error.Message, StringComparison.Ordinal);
        Assert.Contains("'{latest-id:no-such-kind}' names no known kind", error.Message, StringComparison.Ordinal);
        Assert.Contains("unknown placeholder '{nope}'", error.Message, StringComparison.Ordinal);
        Assert.Contains("kind 'unknown-kind': no importer reads this kind.", error.Message, StringComparison.Ordinal);
        Assert.Contains("uses '{month}', which no pattern captures", error.Message, StringComparison.Ordinal);
        Assert.Contains("kind 'onrc-firme', resource pattern:", error.Message, StringComparison.Ordinal);
        Assert.Contains("Job 'Bad Id': the id must be", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownSettingsAreRefusedRatherThanIgnored() =>
        Assert.Throws<System.Text.Json.JsonException>(() => JobDefinitions.Parse("""
            { "timeZone": "UTC", "allowedHosts": ["data.gov.ro"], "jobs": [{ "id": "a", "shedule": "0 1 * * *", "commands": [["search", "build"]] }] }
            """));

    [Theory]
    [InlineData("../outside.csv")]
    [InlineData("a/../../outside.csv")]
    [InlineData("/etc/passwd")]
    [InlineData("a//b.csv")]
    [InlineData("")]
    public void PathsOutsideTheRawFolderAreRefused(string path) => Assert.Null(DiscoverFiles.SafePath("/data/raw", path));

    [Fact]
    public void PathsInsideTheRawFolderAreKept() =>
        Assert.Equal(Path.GetFullPath("/data/raw/seap-history/2027/a.xlsx"), DiscoverFiles.SafePath("/data/raw", "seap-history/2027/a.xlsx"));
}
