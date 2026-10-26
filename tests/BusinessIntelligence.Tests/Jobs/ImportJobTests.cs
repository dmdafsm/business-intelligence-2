using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BusinessIntelligence.Modules.Jobs;
using BusinessIntelligence.Modules.Platform.Persistence;
using BusinessIntelligence.Modules.Registry.Sources;
using BusinessIntelligence.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace BusinessIntelligence.Tests.Jobs;

/// <summary>The catalog of discovered files is process-wide; tests in one class run one at a time, and each resets it.</summary>
public sealed class ImportJobTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IDisposable
{
    private const string Api = "https://ckan.test/api/3/action/";
    private const string Year2026 = "aaaaaaaa-0000-4000-8000-000000002026";
    private const string Year2027 = "aaaaaaaa-0000-4000-8000-000000002027";
    private readonly DirectoryInfo _raw = Directory.CreateTempSubdirectory("bi-jobs-raw-");

    public void Dispose()
    {
        SourceCatalog.Include([]);
        _raw.Delete(recursive: true);
    }

    private static JobDefinitions Seap(long maxFileBytes = 1 << 20, string commands = """[["procurement", "import-all", "{raw}"]]""") => JobDefinitions.Parse($$"""
        {
          "timeZone": "Europe/Bucharest",
          "ckanApi": "{{Api}}",
          "allowedHosts": ["ckan.test"],
          "maxFileBytes": {{maxFileBytes}},
          "jobs": [{
            "id": "seap",
            "schedule": "0 9 * * 3",
            "discover": [{
              "organization": "adr",
              "dataset": "^Achizi(t|ț)ii publice (?<year>\\d{4})$",
              "referenceDate": "{quarterEnd}",
              "year": "{year}",
              "family": "Q{quarter}",
              "map": { "quarter": { "I": "1", "II": "2", "III": "3", "IV": "4" } },
              "files": [
                { "kind": "seap-contracts", "resource": "^Contracte T\\s*(?<quarter>IV|III|II|I|[1-4])(\\s+\\d{4})?$", "id": "seap-contracts-{year}-q{quarter}", "path": "seap-history/{year}/{file}" },
                { "kind": "seap-direct", "resource": "^Achizi(t|ț)ii directe T\\s*(?<quarter>IV|III|II|I|[1-4])(\\s+\\d{4})?$", "id": "seap-direct-{year}-q{quarter}", "path": "seap-history/{year}/{file}" }
              ]
            }],
            "commands": {{commands}}
          }]
        }
        """);

    [Fact]
    public async Task DiscoveryPinsOnlyNewerFilesAndARerunDownloadsNothing()
    {
        await using var database = await postgres.CreateIsolatedContextAsync();
        var portal = new FakePortal();
        portal.Dataset(Year2026, "Achiziții publice 2026", "2026-04-06",
            // Q3 2026 is pinned in the embedded catalog (another resource id): not newer, so skipped.
            ("Contracte TIII 2026", "raport-contracte-tiii-republicat.xlsx", "contracts q3"),
            ("Contracte T IV 2026", "contracte-t_iv_2026.xlsx", "contracts q4 2026"));
        portal.Dataset(Year2027, "Achiziții publice 2027", "2027-04-06",
            ("Contracte TI 2027", "raport-contracte-ti-2027.xlsx", "contracts q1 2027"),
            ("Achizitii directe TI 2027", "raport-achizitii-directe-ti-2027.xlsx", "direct q1 2027"),
            ("Date din modificare contract TI 2027", "raport-date-din-modificare-contract-ti-2027.xlsx", "not imported"));
        portal.Dataset("bbbbbbbb-0000-4000-8000-000000000001", "Plan achizitii 2027", "2027-01-01", ("Contracte TI 2027", "other.xlsx", "other"));
        using var http = new HttpClient(portal);
        var definitions = Seap();
        var lines = new List<string>();

        var result = await new DiscoverFiles(database, http, TimeProvider.System).ExecuteAsync(definitions, definitions.Get("seap"), _raw.FullName, null, false, lines.Add, CancellationToken.None);

        Assert.Empty(result.Errors);
        Assert.Equal(["seap-contracts-2026-q4", "seap-contracts-2027-q1", "seap-direct-2027-q1"], result.Files.Select(file => file.Id).Order());
        var pinned = await database.DiscoveredFiles.AsNoTracking().OrderBy(file => file.Id).ToListAsync();
        Assert.Equal(3, pinned.Count);
        var q4 = pinned[0];
        Assert.Equal(("seap-contracts", new DateOnly(2026, 12, 31), (short?)2026, "Q4", "seap-history/2026/contracte-t_iv_2026.xlsx"),
            (q4.Kind, q4.ReferenceDate, q4.Year, q4.Family, q4.LocalPath));
        Assert.Equal(("Agenția de test", "CC BY 4.0", "seap"), (q4.Publisher, q4.License, q4.JobId));
        Assert.Equal(Sha256("contracts q4 2026"), q4.Sha256);
        Assert.Equal("contracts q4 2026", await File.ReadAllTextAsync(Path.Combine(_raw.FullName, q4.LocalPath)));
        Assert.Equal(new DateOnly(2027, 3, 31), pinned[1].ReferenceDate);
        Assert.Contains("adr: 2 matching datasets checked; 1 files not newer than the catalog ignored.", lines);
        // The importers see the new files through the catalog.
        Assert.Equal("seap-contracts-2027-q1", SourceCatalog.OfKind("seap-contracts").MaxBy(file => file.ReferenceDate)!.Id);
        Assert.Equal(3, portal.Downloads);

        var rerun = await new DiscoverFiles(database, http, TimeProvider.System).ExecuteAsync(definitions, definitions.Get("seap"), _raw.FullName, null, false, _ => { }, CancellationToken.None);
        Assert.Empty(rerun.Files);
        Assert.Equal(3, portal.Downloads);
    }

    [Fact]
    public async Task ADatasetIsPinnedWholeOrNotAtAll()
    {
        await using var database = await postgres.CreateIsolatedContextAsync();
        var portal = new FakePortal();
        portal.Dataset(Year2027, "Achiziții publice 2027", "2027-04-06",
            ("Contracte TI 2027", "raport-contracte-ti-2027.xlsx", "contracts q1 2027"),
            ("Achizitii directe TI 2027", "raport-achizitii-directe-ti-2027.xlsx", "direct q1 2027"));
        portal.Fail("raport-achizitii-directe-ti-2027.xlsx");
        using var http = new HttpClient(portal);
        var definitions = Seap();

        var result = await new DiscoverFiles(database, http, TimeProvider.System).ExecuteAsync(definitions, definitions.Get("seap"), _raw.FullName, null, false, _ => { }, CancellationToken.None);

        Assert.Empty(result.Files);
        Assert.Contains("download failed, nothing of it pinned", Assert.Single(result.Errors), StringComparison.Ordinal);
        Assert.Empty(await database.DiscoveredFiles.ToListAsync());

        // The next run fetches only what is missing: the complete file is reused, not downloaded again.
        portal.Fail(null);
        var retry = await new DiscoverFiles(database, http, TimeProvider.System).ExecuteAsync(definitions, definitions.Get("seap"), _raw.FullName, null, false, _ => { }, CancellationToken.None);
        Assert.Equal(2, retry.Files.Count);
        Assert.Equal(1, portal.DownloadsOf("raport-contracte-ti-2027.xlsx"));
    }

    [Fact]
    public async Task OversizedFilesAndOtherHostsAreNotDownloaded()
    {
        await using var database = await postgres.CreateIsolatedContextAsync();
        var portal = new FakePortal();
        portal.Dataset(Year2027, "Achiziții publice 2027", "2027-04-06",
            ("Contracte TI 2027", "raport-contracte-ti-2027.xlsx", new string('x', 2048)));
        portal.Dataset("aaaaaaaa-0000-4000-8000-000000000099", "Achiziții publice 2028", "2028-04-06",
            ("Contracte TI 2028", "https://elsewhere.test/contracte-2028.xlsx", "contracts"));
        using var http = new HttpClient(portal);
        var definitions = Seap(maxFileBytes: 1024);
        var lines = new List<string>();

        var result = await new DiscoverFiles(database, http, TimeProvider.System).ExecuteAsync(definitions, definitions.Get("seap"), _raw.FullName, null, false, lines.Add, CancellationToken.None);

        Assert.Empty(result.Files);
        Assert.Contains(lines, line => line.Contains("larger than the 1 KB limit", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("not on an allowed https host", StringComparison.Ordinal));
        Assert.Equal(0, portal.Downloads);
        Assert.False(File.Exists(Path.Combine(_raw.FullName, "seap-history", "2027", "raport-contracte-ti-2027.xlsx")));
    }

    [Fact]
    public async Task ADryRunListsNewFilesWithoutDownloadingThem()
    {
        await using var database = await postgres.CreateIsolatedContextAsync();
        var portal = new FakePortal();
        portal.Dataset(Year2027, "Achiziții publice 2027", "2027-04-06", ("Contracte TI 2027", "raport-contracte-ti-2027.xlsx", "contracts"));
        using var http = new HttpClient(portal);
        var definitions = Seap();

        var result = await new DiscoverFiles(database, http, TimeProvider.System).ExecuteAsync(definitions, definitions.Get("seap"), _raw.FullName, null, true, _ => { }, CancellationToken.None);

        Assert.Equal("seap-contracts-2027-q1", Assert.Single(result.Files).Id);
        Assert.Equal(0, portal.Downloads);
        Assert.Empty(await database.DiscoveredFiles.ToListAsync());
    }

    [Fact]
    public async Task ARunSkipsItsCommandsWhenNothingIsNewAndItsLastRunSucceeded()
    {
        await using var database = await postgres.CreateIsolatedContextAsync();
        var connection = database.Database.GetConnectionString()!;
        AppDbContext Create() => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options);
        using var http = new HttpClient(new FakePortal());
        var commands = new FakeCommands();
        var definitions = Seap(commands: """[["procurement", "import-all", "{raw}"], ["registry", "import", "{latest-id:seap-direct}", "{latest-path:seap-direct}"]]""");
        var runner = new RunJob(Create, http, commands, TimeProvider.System);

        // The first run has no previous run, so its commands run even though nothing is new.
        var first = await RunAsync(database, runner, definitions, force: false);
        Assert.Equal(RunStatus.Succeeded, first.Status);
        Assert.Equal(
            [["procurement", "import-all", _raw.FullName], ["registry", "import", "seap-direct-2026-q3", Path.Combine(_raw.FullName, "seap-history/2026/raport-achizitii-directe-tiii.xlsx")]],
            commands.Calls);
        var log = await database.JobRunLines.AsNoTracking().Where(line => line.RunId == first.Id).OrderBy(line => line.Number).Select(line => line.Stream + " " + line.Text).ToListAsync();
        Assert.Contains("job RUN: procurement import-all " + _raw.FullName, log);
        Assert.Contains("out {\"outcome\":\"AlreadyImported\"}", log);
        Assert.Equal("job Run succeeded.", log[^1]);

        var second = await RunAsync(database, runner, definitions, force: false);
        Assert.Equal(RunStatus.Succeeded, second.Status);
        Assert.Equal(2, commands.Calls.Count);

        var forced = await RunAsync(database, runner, definitions, force: true);
        Assert.Equal(RunStatus.Succeeded, forced.Status);
        Assert.Equal(4, commands.Calls.Count);
    }

    [Fact]
    public async Task AFailedCommandFailsTheRunAndTheNextRunRetries()
    {
        await using var database = await postgres.CreateIsolatedContextAsync();
        var connection = database.Database.GetConnectionString()!;
        AppDbContext Create() => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options);
        using var http = new HttpClient(new FakePortal());
        var commands = new FakeCommands { ExitCode = 1 };
        var definitions = Seap(commands: """[["procurement", "import-all", "{raw}"], ["search", "build"]]""");
        var runner = new RunJob(Create, http, commands, TimeProvider.System);

        var failed = await RunAsync(database, runner, definitions, force: false);
        Assert.Equal(RunStatus.Failed, failed.Status);
        Assert.Equal($"procurement import-all {_raw.FullName} exited with 1.", failed.Error);
        Assert.Equal($"procurement import-all {_raw.FullName}", failed.Step);
        Assert.Single(commands.Calls);

        commands.ExitCode = 0;
        var retried = await RunAsync(database, runner, definitions, force: false);
        Assert.Equal(RunStatus.Succeeded, retried.Status);
        Assert.Equal(3, commands.Calls.Count);
    }

    [Fact]
    public async Task TheOperatorCanStopARunningCommand()
    {
        await using var database = await postgres.CreateIsolatedContextAsync();
        var connection = database.Database.GetConnectionString()!;
        AppDbContext Create() => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options);
        using var http = new HttpClient(new FakePortal());
        var commands = new FakeCommands { Hang = true };
        var definitions = Seap(commands: """[["anaf", "harvest"]]""");
        var (run, _) = await JobRuns.EnqueueAsync(database, "seap", RunTrigger.Page, true, TimeProvider.System, CancellationToken.None);
        Assert.Equal(run.Id, await JobRuns.ClaimNextAsync(database, TimeProvider.System, CancellationToken.None));

        var execution = new RunJob(Create, http, commands, TimeProvider.System).ExecuteAsync(definitions, run.Id, _raw.FullName, CancellationToken.None);
        await commands.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(await JobRuns.CancelAsync(database, run.Id, TimeProvider.System, CancellationToken.None));

        Assert.Equal(RunStatus.Cancelled, await execution.WaitAsync(TimeSpan.FromSeconds(15)));
        var stored = await database.JobRuns.AsNoTracking().SingleAsync(item => item.Id == run.Id);
        Assert.Equal(("Cancelled by the operator.", true), (stored.Error, stored.FinishedAt is not null));
    }

    [Fact]
    public async Task TheQueueKeepsOneActiveRunPerJobAndRecoversInterruptedRuns()
    {
        await using var database = await postgres.CreateIsolatedContextAsync();
        var time = TimeProvider.System;
        var (first, created) = await JobRuns.EnqueueAsync(database, "seap", RunTrigger.Page, false, time, CancellationToken.None);
        Assert.True(created);
        var (again, createdAgain) = await JobRuns.EnqueueAsync(database, "seap", RunTrigger.Cli, true, time, CancellationToken.None);
        Assert.Equal((first.Id, false), (again.Id, createdAgain));
        var (other, _) = await JobRuns.EnqueueAsync(database, "onrc", RunTrigger.Page, false, time, CancellationToken.None);

        // The database refuses a second active run even when the check above is raced.
        database.JobRuns.Add(new JobRun { JobId = "seap", Trigger = RunTrigger.Page, Status = RunStatus.Queued, QueuedAt = time.GetUtcNow() });
        var duplicate = await Assert.ThrowsAsync<DbUpdateException>(() => database.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, ((PostgresException)duplicate.InnerException!).SqlState);
        database.ChangeTracker.Clear();

        Assert.Equal(first.Id, await JobRuns.ClaimNextAsync(database, time, CancellationToken.None));
        Assert.True(await JobRuns.CancelAsync(database, other.Id, time, CancellationToken.None));
        Assert.Null(await JobRuns.ClaimNextAsync(database, time, CancellationToken.None));

        Assert.Equal(1, await JobRuns.RecoverInterruptedAsync(database, time, CancellationToken.None));
        var runs = await database.JobRuns.AsNoTracking().OrderBy(run => run.Id).ToListAsync();
        Assert.Equal([RunStatus.Failed, RunStatus.Cancelled], runs.Select(run => run.Status));
        Assert.Equal("Interrupted: the worker stopped during the run.", runs[0].Error);
    }

    [Fact]
    public async Task TheScheduleQueuesADueJobOnce()
    {
        await using var database = await postgres.CreateIsolatedContextAsync();
        var definitions = Seap();
        // Wednesday 2027-01-06, 10:00 in Bucharest: the Wednesday 09:00 run is due if the worker started before it.
        var now = new FixedTime(new DateTimeOffset(2027, 1, 6, 8, 0, 0, TimeSpan.Zero));

        Assert.Empty(await JobRuns.QueueDueAsync(database, definitions, now.GetUtcNow().AddMinutes(-30), now, CancellationToken.None));
        Assert.Equal(["seap"], await JobRuns.QueueDueAsync(database, definitions, now.GetUtcNow().AddHours(-2), now, CancellationToken.None));
        Assert.Empty(await JobRuns.QueueDueAsync(database, definitions, now.GetUtcNow().AddHours(-2), now, CancellationToken.None));

        // Once that run finished, the next time follows it: a week later.
        await database.JobRuns.ExecuteUpdateAsync(update => update.SetProperty(run => run.Status, RunStatus.Succeeded));
        Assert.Empty(await JobRuns.QueueDueAsync(database, definitions, now.GetUtcNow().AddHours(-2), now, CancellationToken.None));
        var week = new FixedTime(now.GetUtcNow().AddDays(7));
        Assert.Equal(["seap"], await JobRuns.QueueDueAsync(database, definitions, now.GetUtcNow().AddHours(-2), week, CancellationToken.None));
    }

    [Fact]
    public async Task AChildProcessReportsItsOutputAndExitCode()
    {
        var lines = new List<string>();
        var exit = await new ProcessCommandRunner("/bin/sh", ["-c", "echo \"out $1\"; echo err >&2; exit 3", "sh"])
            .RunAsync(["value"], (stream, text) => { lines.Add($"{stream} {text}"); return Task.CompletedTask; }, CancellationToken.None);
        Assert.Equal(3, exit);
        Assert.Equal(["err err", "out out value"], lines.Order());
    }

    private async Task<JobRun> RunAsync(AppDbContext database, RunJob runner, JobDefinitions definitions, bool force)
    {
        var (run, _) = await JobRuns.EnqueueAsync(database, "seap", RunTrigger.Cli, force, TimeProvider.System, CancellationToken.None);
        Assert.Equal(run.Id, await JobRuns.ClaimNextAsync(database, TimeProvider.System, CancellationToken.None));
        await runner.ExecuteAsync(definitions, run.Id, _raw.FullName, CancellationToken.None);
        return await database.JobRuns.AsNoTracking().SingleAsync(item => item.Id == run.Id);
    }

    private static string Sha256(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeCommands : ICommandRunner
    {
        public List<string[]> Calls { get; } = [];
        public int ExitCode { get; set; }
        public bool Hang { get; init; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<int> RunAsync(IReadOnlyList<string> arguments, Func<string, string, Task> line, CancellationToken cancellationToken)
        {
            Calls.Add([.. arguments]);
            Started.TrySetResult();
            if (Hang)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            await line("out", """{"outcome":"AlreadyImported"}""");
            return ExitCode;
        }
    }

    /// <summary>A CKAN portal at https://ckan.test: package_search answers with the datasets added, newest first.</summary>
    private sealed class FakePortal : HttpMessageHandler
    {
        private readonly List<(string Id, string Title, string Created, (string Name, string File, string Content)[] Resources)> _datasets = [];
        private readonly Dictionary<string, int> _downloads = [];
        private string? _failing;

        public int Downloads => _downloads.Values.Sum();

        public int DownloadsOf(string file) => _downloads.GetValueOrDefault(file);

        public void Dataset(string id, string title, string created, params (string Name, string File, string Content)[] resources) =>
            _datasets.Add((id, title, created, resources));

        public void Fail(string? file) => _failing = file;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!;
            if (url.AbsoluteUri.StartsWith(Api + "package_search", StringComparison.Ordinal))
            {
                Assert.Contains("organization%3Aadr", url.Query, StringComparison.Ordinal);
                var results = _datasets.OrderByDescending(item => item.Created, StringComparer.Ordinal).Select(dataset => new
                {
                    id = dataset.Id,
                    title = dataset.Title,
                    metadata_created = dataset.Created + "T10:00:00",
                    license_title = "CC BY 4.0",
                    organization = new { title = "Agenția de test" },
                    resources = dataset.Resources.Select((resource, index) => new
                    {
                        id = $"{dataset.Id[..^8]}{dataset.Id[^4..]}{index:0000}",
                        name = resource.Name,
                        url = resource.File.StartsWith("https://", StringComparison.Ordinal) ? resource.File
                            : $"https://ckan.test/dataset/{dataset.Id}/resource/{dataset.Id[..^8]}{dataset.Id[^4..]}{index:0000}/download/{resource.File}",
                        last_modified = dataset.Created + "T10:00:00.123456",
                        size = Encoding.UTF8.GetByteCount(resource.Content),
                    }),
                });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new { success = true, result = new { results } }), Encoding.UTF8, "application/json"),
                });
            }

            var file = Uri.UnescapeDataString(url.Segments[^1]);
            var content = _datasets.SelectMany(dataset => dataset.Resources).FirstOrDefault(resource => resource.File == file).Content;
            if (url.Host != "ckan.test" || content is null || file == _failing)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            }

            _downloads[file] = _downloads.GetValueOrDefault(file) + 1;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content, Encoding.UTF8), RequestMessage = request });
        }
    }
}
