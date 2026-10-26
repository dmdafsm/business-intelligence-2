using BusinessIntelligence.Modules.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BusinessIntelligence.Modules.Jobs;

/// <summary>The run queue: one active (queued or running) run per job, claimed oldest first.</summary>
public static class JobRuns
{
    /// <summary>Queues a run, or returns the job's run that is already queued or running.</summary>
    public static async Task<(JobRun Run, bool Created)> EnqueueAsync(
        AppDbContext database, string jobId, string trigger, bool force, TimeProvider time, CancellationToken cancellationToken)
    {
        if (await ActiveAsync(database, jobId, cancellationToken) is { } active)
        {
            return (active, false);
        }

        var run = new JobRun { JobId = jobId, Trigger = trigger, Status = RunStatus.Queued, Force = force, QueuedAt = time.GetUtcNow() };
        database.JobRuns.Add(run);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return (run, true);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Queued by someone else at the same moment.
            database.Entry(run).State = EntityState.Detached;
            return (await ActiveAsync(database, jobId, cancellationToken) ?? throw new InvalidOperationException("The active run disappeared."), false);
        }
    }

    /// <summary>Marks the oldest queued run as running and returns its id.</summary>
    public static async Task<long?> ClaimNextAsync(AppDbContext database, TimeProvider time, CancellationToken cancellationToken)
    {
        var claimed = await database.Database.SqlQuery<long>($"""
            UPDATE jobs.runs SET status = {RunStatus.Running}, started_at = {time.GetUtcNow()}
            WHERE id = (SELECT id FROM jobs.runs WHERE status = {RunStatus.Queued} ORDER BY queued_at, id LIMIT 1 FOR UPDATE SKIP LOCKED)
            RETURNING id AS "Value"
            """).ToListAsync(cancellationToken);
        return claimed.Count == 0 ? null : claimed[0];
    }

    /// <summary>Starts a run that is still queued (for <c>jobs run</c>, which runs one job in its own process).</summary>
    public static async Task<bool> StartAsync(AppDbContext database, long runId, TimeProvider time, CancellationToken cancellationToken) =>
        await database.JobRuns.Where(run => run.Id == runId && run.Status == RunStatus.Queued).ExecuteUpdateAsync(update => update
            .SetProperty(run => run.Status, RunStatus.Running)
            .SetProperty(run => run.StartedAt, time.GetUtcNow()), cancellationToken) > 0;

    /// <summary>Fails the runs left running by a stopped worker. Call it only while holding the <see cref="JobLock"/>.</summary>
    public static Task<int> RecoverInterruptedAsync(AppDbContext database, TimeProvider time, CancellationToken cancellationToken) =>
        database.JobRuns.Where(run => run.Status == RunStatus.Running).ExecuteUpdateAsync(update => update
            .SetProperty(run => run.Status, RunStatus.Failed)
            .SetProperty(run => run.FinishedAt, time.GetUtcNow())
            .SetProperty(run => run.Error, "Interrupted: the worker stopped during the run."), cancellationToken);

    /// <summary>Cancels a queued run at once; a running run is asked to stop and stops within seconds.</summary>
    public static async Task<bool> CancelAsync(AppDbContext database, long runId, TimeProvider time, CancellationToken cancellationToken)
    {
        var queued = await database.JobRuns.Where(run => run.Id == runId && run.Status == RunStatus.Queued).ExecuteUpdateAsync(update => update
            .SetProperty(run => run.Status, RunStatus.Cancelled)
            .SetProperty(run => run.FinishedAt, time.GetUtcNow()), cancellationToken);
        if (queued > 0)
        {
            return true;
        }

        return await database.JobRuns.Where(run => run.Id == runId && run.Status == RunStatus.Running)
            .ExecuteUpdateAsync(update => update.SetProperty(run => run.CancelRequested, true), cancellationToken) > 0;
    }

    /// <summary>
    /// Queues every scheduled job whose next time has come. The next time follows the job's last scheduled run, or
    /// <paramref name="since"/> (the worker's start) when there is none; missed times collapse into one run.
    /// </summary>
    public static async Task<IReadOnlyList<string>> QueueDueAsync(
        AppDbContext database, JobDefinitions definitions, DateTimeOffset since, TimeProvider time, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var last = await LastScheduledAsync(database, cancellationToken);
        var queued = new List<string>();
        foreach (var job in definitions.Jobs.Where(job => job.Schedule is not null))
        {
            var next = job.Schedule!.Next(last.TryGetValue(job.Id, out var at) && at > since ? at : since, definitions.Zone);
            if (next <= now && (await EnqueueAsync(database, job.Id, RunTrigger.Schedule, false, time, cancellationToken)).Created)
            {
                queued.Add(job.Id);
            }
        }

        return queued;
    }

    public static async Task<Dictionary<string, DateTimeOffset>> LastScheduledAsync(AppDbContext database, CancellationToken cancellationToken) =>
        await database.JobRuns.AsNoTracking().Where(run => run.Trigger == RunTrigger.Schedule)
            .GroupBy(run => run.JobId).Select(group => new { group.Key, Last = group.Max(run => run.QueuedAt) })
            .ToDictionaryAsync(item => item.Key, item => item.Last, cancellationToken);

    private static Task<JobRun?> ActiveAsync(AppDbContext database, string jobId, CancellationToken cancellationToken) =>
        database.JobRuns.AsNoTracking().FirstOrDefaultAsync(run => run.JobId == jobId && (run.Status == RunStatus.Queued || run.Status == RunStatus.Running), cancellationToken);
}

/// <summary>
/// The session-level PostgreSQL advisory lock that lets one process at a time run jobs (the worker, or <c>jobs run</c>).
/// It is held while the connection is open.
/// </summary>
public sealed class JobLock : IAsyncDisposable
{
    private const long Key = 0x626932_6A6F6273;
    private readonly NpgsqlConnection _connection;

    private JobLock(NpgsqlConnection connection) => _connection = connection;

    public static async Task<JobLock?> TryAcquireAsync(string connectionString, CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", connection);
        command.Parameters.AddWithValue("key", Key);
        if (await command.ExecuteScalarAsync(cancellationToken) is true)
        {
            return new JobLock(connection);
        }

        await connection.DisposeAsync();
        return null;
    }

    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}
