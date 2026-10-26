using BusinessIntelligence.Modules.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BusinessIntelligence.Modules.Jobs;

/// <summary>
/// Executes one claimed run: discovery first, then the job's commands in order, each as its own process. The run's
/// output goes to <c>jobs.run_lines</c>. Commands are skipped when discovery found nothing new and the job's previous
/// run succeeded, unless the run is forced. Each importer publishes atomically, so a failed run leaves the published
/// data as it was.
/// </summary>
public sealed class RunJob(Func<AppDbContext> createDatabase, HttpClient http, ICommandRunner commands, TimeProvider time, Action<string, string>? echo = null)
{
    public const int MaxLines = 50_000;

    public async Task<string> ExecuteAsync(JobDefinitions definitions, long runId, string rawRoot, CancellationToken cancellationToken)
    {
        JobRun run;
        await using (var database = createDatabase())
        {
            run = await database.JobRuns.AsNoTracking().SingleAsync(item => item.Id == runId, cancellationToken);
        }

        await using var log = new RunLog(createDatabase, runId, time, echo);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var watch = WatchCancellationAsync(runId, stop, stop.Token);
        var status = RunStatus.Failed;
        string? error = null;
        var newFiles = 0;
        try
        {
            var job = definitions.Get(run.JobId);
            stop.CancelAfter(job.Timeout);
            await log.WriteAsync("job", $"Run {runId} of {job.Id} ({run.Trigger}{(run.Force ? ", forced" : string.Empty)}).");
            var discoveryErrors = new List<string>();
            if (job.Discover.Count > 0)
            {
                await SetStepAsync(runId, "discover", null, stop.Token);
                await using var database = createDatabase();
                var lines = new List<string>();
                var result = await new DiscoverFiles(database, http, time).ExecuteAsync(definitions, job, rawRoot, runId, false, lines.Add, stop.Token);
                foreach (var line in lines)
                {
                    await log.WriteAsync("job", line);
                }

                newFiles = result.Files.Count;
                discoveryErrors.AddRange(result.Errors);
                await log.WriteAsync("job", $"Discovery pinned {newFiles} new files.");
                await SetStepAsync(runId, "discover", newFiles, stop.Token);
            }

            if (job.Discover.Count > 0 && newFiles == 0 && !run.Force && await PreviousSucceededAsync(run, stop.Token))
            {
                await log.WriteAsync("job", "Nothing new and the previous run succeeded: commands skipped.");
            }
            else
            {
                foreach (var command in job.Commands)
                {
                    var expanded = JobDefinitions.Expand(command, rawRoot);
                    var text = string.Join(' ', expanded);
                    await SetStepAsync(runId, text, null, stop.Token);
                    await log.WriteAsync("job", "RUN: " + text);
                    var started = time.GetTimestamp();
                    var exit = await commands.RunAsync(expanded, log.WriteAsync, stop.Token);
                    await log.WriteAsync("job", $"{(exit == 0 ? "DONE" : "FAIL")}: {text} in {time.GetElapsedTime(started).TotalSeconds:0} s (exit {exit}).");
                    if (exit != 0)
                    {
                        throw new JobFailedException($"{text} exited with {exit}.");
                    }
                }
            }

            if (discoveryErrors.Count > 0)
            {
                throw new JobFailedException(string.Join(" ", discoveryErrors));
            }

            status = RunStatus.Succeeded;
        }
        catch (OperationCanceledException)
        {
            status = !cancellationToken.IsCancellationRequested && await CancelRequestedAsync(runId) ? RunStatus.Cancelled : RunStatus.Failed;
            error = cancellationToken.IsCancellationRequested ? "Interrupted: the worker stopped during the run."
                : status == RunStatus.Cancelled ? "Cancelled by the operator." : "Stopped: the job's time limit was reached.";
        }
        catch (Exception exception) when (exception is JobFailedException or HttpRequestException or InvalidDataException or InvalidOperationException
            or ArgumentException or IOException or System.Text.Json.JsonException)
        {
            error = exception.Message;
        }
        finally
        {
            await stop.CancelAsync();
            await watch;
        }

        await log.WriteAsync("job", error is null ? $"Run {status}." : $"Run {status}: {error}");
        await log.CompleteAsync();
        // The final state is written even when the worker is stopping, so the run is not left "running".
        await using (var database = createDatabase())
        {
            await database.JobRuns.Where(item => item.Id == runId).ExecuteUpdateAsync(update => update
                .SetProperty(item => item.Status, status)
                .SetProperty(item => item.Error, error)
                .SetProperty(item => item.FinishedAt, time.GetUtcNow()), CancellationToken.None);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return status;
    }

    private async Task<bool> PreviousSucceededAsync(JobRun run, CancellationToken cancellationToken)
    {
        await using var database = createDatabase();
        var previous = await database.JobRuns.AsNoTracking()
            .Where(item => item.JobId == run.JobId && item.Id < run.Id && (item.Status == RunStatus.Succeeded || item.Status == RunStatus.Failed))
            .OrderByDescending(item => item.Id).Select(item => item.Status).FirstOrDefaultAsync(cancellationToken);
        return previous == RunStatus.Succeeded;
    }

    private async Task SetStepAsync(long runId, string step, int? newFiles, CancellationToken cancellationToken)
    {
        await using var database = createDatabase();
        await database.JobRuns.Where(item => item.Id == runId).ExecuteUpdateAsync(update => update
            .SetProperty(item => item.Step, step)
            .SetProperty(item => item.NewFiles, item => newFiles ?? item.NewFiles), cancellationToken);
    }

    private async Task<bool> CancelRequestedAsync(long runId)
    {
        await using var database = createDatabase();
        return await database.JobRuns.AsNoTracking().Where(item => item.Id == runId).Select(item => item.CancelRequested).SingleAsync(CancellationToken.None);
    }

    /// <summary>Stops the run when the operator asks for it (polled every few seconds).</summary>
    private async Task WatchCancellationAsync(long runId, CancellationTokenSource stop, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(3), time, cancellationToken);
                await using var database = createDatabase();
                if (await database.JobRuns.AsNoTracking().Where(item => item.Id == runId).Select(item => item.CancelRequested).SingleAsync(cancellationToken))
                {
                    await stop.CancelAsync();
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The run ended.
        }
    }

    private sealed class JobFailedException(string message) : Exception(message);

    /// <summary>Buffers output lines and writes them in batches; after <see cref="MaxLines"/> lines only a count is kept.</summary>
    private sealed class RunLog(Func<AppDbContext> createDatabase, long runId, TimeProvider time, Action<string, string>? echo) : IAsyncDisposable
    {
        private readonly List<JobRunLine> _pending = [];
        private int _number;
        private long _dropped;
        private long _lastFlush = time.GetTimestamp();

        public async Task WriteAsync(string stream, string text)
        {
            echo?.Invoke(stream, text);
            if (_number >= MaxLines)
            {
                _dropped++;
                return;
            }

            _pending.Add(new JobRunLine { RunId = runId, Number = ++_number, At = time.GetUtcNow(), Stream = stream, Text = text.Length > 4000 ? text[..4000] + "…" : text });
            // The runner's own lines (the step starting, its result) are written at once, so the page shows the current step.
            if (stream == "job" || _pending.Count >= 200 || time.GetElapsedTime(_lastFlush) > TimeSpan.FromSeconds(2))
            {
                await FlushAsync();
            }
        }

        public async Task CompleteAsync()
        {
            if (_dropped > 0)
            {
                _pending.Add(new JobRunLine { RunId = runId, Number = ++_number, At = time.GetUtcNow(), Stream = "job", Text = $"Output truncated: {_dropped} more lines not kept." });
                _dropped = 0;
            }

            await FlushAsync();
        }

        public async Task FlushAsync()
        {
            if (_pending.Count == 0)
            {
                return;
            }

            await using var database = createDatabase();
            database.JobRunLines.AddRange(_pending);
            await database.SaveChangesAsync(CancellationToken.None);
            _pending.Clear();
            _lastFlush = time.GetTimestamp();
        }

        public async ValueTask DisposeAsync() => await FlushAsync();
    }
}
