using BusinessIntelligence.Modules.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BusinessIntelligence.Modules.Jobs;

/// <summary>What the operator page and <c>jobs list</c> show: each job with its last and next run, recent runs and files.</summary>
public sealed record JobOverview(IReadOnlyList<JobSummary> Jobs, IReadOnlyList<JobRun> RecentRuns, IReadOnlyList<DiscoveredFile> RecentFiles)
{
    public static async Task<JobOverview> LoadAsync(AppDbContext database, JobDefinitions definitions, TimeProvider time, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var lastScheduled = await JobRuns.LastScheduledAsync(database, cancellationToken);
        var lastIds = await database.JobRuns.AsNoTracking().GroupBy(run => run.JobId).Select(group => group.Max(run => run.Id)).ToListAsync(cancellationToken);
        var last = await database.JobRuns.AsNoTracking().Where(run => lastIds.Contains(run.Id)).ToDictionaryAsync(run => run.JobId, cancellationToken);
        var jobs = definitions.Jobs.Select(job => new JobSummary(
            job.Id, job.Title, job.Schedule?.Expression,
            job.Schedule?.Next(lastScheduled.TryGetValue(job.Id, out var at) && at > now.AddDays(-400) ? at : now, definitions.Zone),
            last.GetValueOrDefault(job.Id), job.Discover.Count, [.. job.Commands.Select(command => string.Join(' ', command))])).ToList();
        var runs = await database.JobRuns.AsNoTracking().OrderByDescending(run => run.Id).Take(30).ToListAsync(cancellationToken);
        var files = await database.DiscoveredFiles.AsNoTracking().OrderByDescending(file => file.DiscoveredAt).ThenBy(file => file.Id).Take(30).ToListAsync(cancellationToken);
        return new JobOverview(jobs, runs, files);
    }
}

public sealed record JobSummary(
    string Id, string Title, string? Schedule, DateTimeOffset? NextRun, JobRun? LastRun, int DiscoveryRules, IReadOnlyList<string> Commands)
{
    public bool Active => LastRun?.Status is RunStatus.Queued or RunStatus.Running;
}
