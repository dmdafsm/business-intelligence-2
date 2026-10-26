using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using BusinessIntelligence.Modules.Jobs;
using BusinessIntelligence.Modules.Registry.Sources;
using Microsoft.EntityFrameworkCore;

namespace BusinessIntelligence.Host.Composition;

/// <summary>Operator commands for the import jobs of <c>config/import-jobs.json</c>, and the worker that runs them.</summary>
internal static class JobsCommands
{
    private const string Usage = """
        Commands:
          jobs check                  validate the job file
          jobs list                   jobs, schedules, last and next runs
          jobs discover <job>         what discovery would download now (downloads nothing)
          jobs run <job> [--force]    run a job in this process now (refused while the worker runs)
          jobs queue <job> [--force]  queue a run for the worker
          jobs cancel <run-id>        cancel a queued run, or stop a running one
          jobs runs [<job>]           the last 20 runs
          jobs log <run-id>           the output of a run
          jobs worker                 run the schedule and the queue until stopped
        --force runs the commands even when discovery finds nothing new.
        Environment: Jobs__File (default config/import-jobs.json), Jobs__RawRoot (default .research/raw).
        """;

    private static readonly JsonSerializerOptions Output = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string JobFile => Environment.GetEnvironmentVariable("Jobs__File") ?? "config/import-jobs.json";

    private static string RawRoot => Environment.GetEnvironmentVariable("Jobs__RawRoot") ?? ".research/raw";

    /// <summary>Touched by the worker every 20 s, also during a long run; the container healthcheck reads its age.</summary>
    private static string Heartbeat => Path.Combine(Path.GetTempPath(), "bi-worker-heartbeat");

    /// <summary>The worker's healthcheck: its heartbeat is less than 90 s old.</summary>
    public static bool WorkerAlive() =>
        File.Exists(Heartbeat) && DateTime.UtcNow - File.GetLastWriteTimeUtc(Heartbeat) < TimeSpan.FromSeconds(90);

    public static async Task<int> ExecuteAsync(string[] arguments, CancellationToken cancellationToken)
    {
        if (arguments is not (["jobs", "check" or "list" or "worker"] or ["jobs", "discover" or "cancel" or "log", _] or ["jobs", "runs", ..]
            or ["jobs", "run" or "queue", _] or ["jobs", "run" or "queue", _, "--force"]))
        {
            await Console.Error.WriteLineAsync(Usage);
            return 2;
        }

        try
        {
            var definitions = await JobDefinitions.LoadAsync(JobFile, cancellationToken);
            if (arguments is ["jobs", "check"])
            {
                await WriteAsync(new { file = JobFile, valid = true, jobs = definitions.Jobs.Select(job => job.Id) }, cancellationToken);
                return 0;
            }

            var connection = await OperatorDatabase.ResolveConnectionAsync(cancellationToken);
            if (connection is null)
            {
                return 2;
            }

            switch (arguments)
            {
                case ["jobs", "list"]:
                    await ListAsync(connection, definitions, cancellationToken);
                    return 0;
                case ["jobs", "discover", var id]:
                    return await DiscoverAsync(connection, definitions, definitions.Get(id), cancellationToken);
                case ["jobs", "queue", var id, ..]:
                    await using (var database = OperatorDatabase.Create(connection))
                    {
                        var (run, created) = await JobRuns.EnqueueAsync(database, definitions.Get(id).Id, RunTrigger.Cli, arguments.Length == 4, TimeProvider.System, cancellationToken);
                        await WriteAsync(new { run.Id, run.JobId, run.Status, queued = created }, cancellationToken);
                    }

                    return 0;
                case ["jobs", "run", var id, ..]:
                    return await RunHereAsync(connection, definitions, definitions.Get(id), arguments.Length == 4, cancellationToken);
                case ["jobs", "cancel", var runId]:
                    await using (var database = OperatorDatabase.Create(connection))
                    {
                        var cancelled = await JobRuns.CancelAsync(database, long.Parse(runId, CultureInfo.InvariantCulture), TimeProvider.System, cancellationToken);
                        await WriteAsync(new { run = runId, cancelled }, cancellationToken);
                        return cancelled ? 0 : 1;
                    }

                case ["jobs", "runs", ..]:
                    await using (var database = OperatorDatabase.Create(connection))
                    {
                        var job = arguments.Length > 2 ? arguments[2] : null;
                        await WriteAsync(await database.JobRuns.AsNoTracking().Where(run => job == null || run.JobId == job)
                            .OrderByDescending(run => run.Id).Take(20).ToListAsync(cancellationToken), cancellationToken);
                    }

                    return 0;
                case ["jobs", "log", var runId]:
                    await using (var database = OperatorDatabase.Create(connection))
                    {
                        var id = long.Parse(runId, CultureInfo.InvariantCulture);
                        await foreach (var line in database.JobRunLines.AsNoTracking().Where(item => item.RunId == id).OrderBy(item => item.Number)
                            .AsAsyncEnumerable().WithCancellation(cancellationToken))
                        {
                            Console.WriteLine($"{line.At:HH:mm:ss} {line.Stream} {line.Text}");
                        }
                    }

                    return 0;
                default:
                    return await WorkerAsync(connection, cancellationToken);
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or FormatException or JsonException or FileNotFoundException)
        {
            await Console.Error.WriteLineAsync("Jobs: " + exception.Message);
            return 1;
        }
        catch (DbException exception)
        {
            await Console.Error.WriteLineAsync("Database operation failed: " + exception.Message);
            return 1;
        }
    }

    private static async Task ListAsync(string connection, JobDefinitions definitions, CancellationToken cancellationToken)
    {
        await using var database = OperatorDatabase.Create(connection);
        var overview = await JobOverview.LoadAsync(database, definitions, TimeProvider.System, cancellationToken);
        await WriteAsync(overview.Jobs.Select(job => new
        {
            job.Id,
            job.Title,
            schedule = job.Schedule,
            nextRun = job.NextRun,
            lastRun = job.LastRun is { } run ? new { run.Id, run.Status, run.Trigger, run.QueuedAt, run.FinishedAt, run.NewFiles, run.Error } : null,
        }), cancellationToken);
    }

    private static async Task<int> DiscoverAsync(string connection, JobDefinitions definitions, JobDefinition job, CancellationToken cancellationToken)
    {
        await using var database = OperatorDatabase.Create(connection);
        using var http = CreateHttpClient();
        var result = await new DiscoverFiles(database, http, TimeProvider.System).ExecuteAsync(definitions, job, RawRoot, null, true, Console.Error.WriteLine, cancellationToken);
        await WriteAsync(new
        {
            wouldDownload = result.Files.Select(file => new { file.Id, file.Kind, file.ReferenceDate, file.Year, file.Family, file.Bytes, file.LocalPath, file.Url }),
            result.Errors,
        }, cancellationToken);
        return result.Errors.Count == 0 ? 0 : 1;
    }

    private static async Task<int> RunHereAsync(string connection, JobDefinitions definitions, JobDefinition job, bool force, CancellationToken cancellationToken)
    {
        await using var jobLock = await JobLock.TryAcquireAsync(connection, cancellationToken);
        if (jobLock is null)
        {
            await Console.Error.WriteLineAsync("The worker (or another jobs run) is running jobs; use jobs queue instead.");
            return 1;
        }

        long runId;
        await using (var database = OperatorDatabase.Create(connection))
        {
            await JobRuns.RecoverInterruptedAsync(database, TimeProvider.System, cancellationToken);
            var (run, created) = await JobRuns.EnqueueAsync(database, job.Id, RunTrigger.Cli, force, TimeProvider.System, cancellationToken);
            if (!created || !await JobRuns.StartAsync(database, run.Id, TimeProvider.System, cancellationToken))
            {
                await Console.Error.WriteLineAsync($"Run {run.Id} of {job.Id} is already {run.Status}.");
                return 1;
            }

            runId = run.Id;
        }

        using var http = CreateHttpClient();
        var status = await new RunJob(() => OperatorDatabase.Create(connection), http, ProcessCommandRunner.ForCurrentProcess(), TimeProvider.System,
            (stream, text) => (stream == "err" ? Console.Error : Console.Out).WriteLine(text)).ExecuteAsync(definitions, runId, RawRoot, cancellationToken);
        return status == RunStatus.Succeeded ? 0 : 1;
    }

    /// <summary>
    /// Holds the job lock, queues scheduled runs and executes queued ones, one at a time. The job file is read again on
    /// every pass, so an edited schedule or rule applies without a restart; an invalid edit keeps the last valid file.
    /// </summary>
    private static async Task<int> WorkerAsync(string connection, CancellationToken cancellationToken)
    {
        var heartbeat = BeatAsync(cancellationToken);
        JobLock? jobLock;
        while ((jobLock = await JobLock.TryAcquireAsync(connection, cancellationToken)) is null)
        {
            Console.WriteLine("Another process is running jobs; waiting for it.");
            await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
        }

        await using (jobLock)
        {
            var started = DateTimeOffset.UtcNow;
            await using (var database = OperatorDatabase.Create(connection))
            {
                var recovered = await JobRuns.RecoverInterruptedAsync(database, TimeProvider.System, cancellationToken);
                Console.WriteLine($"Worker started; {recovered} interrupted runs marked failed.");
            }

            using var http = CreateHttpClient();
            var runner = new RunJob(() => OperatorDatabase.Create(connection), http, ProcessCommandRunner.ForCurrentProcess(), TimeProvider.System);
            var definitions = await JobDefinitions.LoadAsync(JobFile, cancellationToken);
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        definitions = await JobDefinitions.LoadAsync(JobFile, cancellationToken);
                    }
                    catch (Exception exception) when (exception is InvalidDataException or JsonException or IOException)
                    {
                        Console.Error.WriteLine("The job file is not valid; the last valid one stays in use. " + exception.Message);
                    }

                    long? runId;
                    await using (var database = OperatorDatabase.Create(connection))
                    {
                        foreach (var queued in await JobRuns.QueueDueAsync(database, definitions, started, TimeProvider.System, cancellationToken))
                        {
                            Console.WriteLine($"Scheduled run of {queued} queued.");
                        }

                        runId = await JobRuns.ClaimNextAsync(database, TimeProvider.System, cancellationToken);
                    }

                    if (runId is { } id)
                    {
                        Console.WriteLine($"Run {id} started.");
                        var status = await runner.ExecuteAsync(definitions, id, RawRoot, cancellationToken);
                        Console.WriteLine($"Run {id} {status}.");
                        continue;
                    }

                    await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Console.WriteLine("Worker stopped.");
            }
        }

        await heartbeat;
        return 0;
    }

    private static async Task BeatAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(20));
        try
        {
            do
            {
                await File.WriteAllTextAsync(Heartbeat, DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture), CancellationToken.None);
            }
            while (await timer.WaitForNextTickAsync(cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static HttpClient CreateHttpClient()
    {
        // Each request sets its own time limit; a national file can take minutes.
        var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("business-intelligence-2/1.0 (import jobs)");
        return http;
    }

    /// <summary>Adds the files import jobs pinned to the catalog, so every command sees them.</summary>
    public static async Task IncludeDiscoveredFilesAsync(CancellationToken cancellationToken)
    {
        var connection = await Modules.Platform.Persistence.ApplicationDatabaseConnection.ResolveAsync(
            Environment.GetEnvironmentVariable("ConnectionStrings__Application"), Environment.GetEnvironmentVariable("Database__PasswordFile"), cancellationToken);
        if (string.IsNullOrWhiteSpace(connection))
        {
            return;
        }

        await using var database = OperatorDatabase.Create(connection);
        try
        {
            SourceCatalog.Include((await database.DiscoveredFiles.AsNoTracking().ToListAsync(cancellationToken)).Select(file => file.ToCatalogFile()));
        }
        catch (Npgsql.PostgresException exception) when (exception.SqlState == Npgsql.PostgresErrorCodes.UndefinedTable)
        {
            // Before the jobs migration there are no discovered files.
        }
    }

    private static async Task WriteAsync(object value, CancellationToken cancellationToken)
    {
        await using var stdout = Console.OpenStandardOutput();
        await JsonSerializer.SerializeAsync(stdout, value, Output, cancellationToken);
        await stdout.WriteAsync("\n"u8.ToArray(), cancellationToken);
    }
}
