using BusinessIntelligence.Modules.Registry.Sources;
using Microsoft.EntityFrameworkCore;

namespace BusinessIntelligence.Modules.Jobs;

/// <summary>A source file an import job found, downloaded and pinned; it joins the embedded catalog.</summary>
public sealed class DiscoveredFile
{
    public required string Id { get; set; }
    public required string Kind { get; set; }
    public required string Dataset { get; set; }
    public required string FileName { get; set; }
    public required string Url { get; set; }
    public required string Publisher { get; set; }
    public required string License { get; set; }
    public DateOnly ReferenceDate { get; set; }
    public long Bytes { get; set; }
    public required string Sha256 { get; set; }
    public short? Year { get; set; }
    public string? Family { get; set; }
    public required string LocalPath { get; set; }

    /// <summary>The comparison group of the rule that found it (for example a programme); empty when the kind is one series.</summary>
    public required string Grouping { get; set; }
    public required string JobId { get; set; }
    public long? RunId { get; set; }

    /// <summary>The CKAN resource and the time the publisher last changed it.</summary>
    public required string ResourceId { get; set; }
    public DateTimeOffset? ResourceModified { get; set; }
    public DateTimeOffset DiscoveredAt { get; set; }

    public CatalogFile ToCatalogFile() =>
        new(Id, Kind, Dataset, FileName, new Uri(Url), Publisher, License, ReferenceDate, Bytes, Sha256, Year, Family, LocalPath);
}

public static class RunStatus
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
}

public static class RunTrigger
{
    public const string Schedule = "schedule";
    public const string Page = "page";
    public const string Cli = "cli";
}

public sealed class JobRun
{
    public long Id { get; set; }
    public required string JobId { get; set; }
    public required string Trigger { get; set; }
    public required string Status { get; set; }

    /// <summary>Run the commands even when discovery found nothing new.</summary>
    public bool Force { get; set; }
    public bool CancelRequested { get; set; }
    public DateTimeOffset QueuedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>The step running now, or the one that failed.</summary>
    public string? Step { get; set; }
    public int NewFiles { get; set; }
    public string? Error { get; set; }
}

public sealed class JobRunLine
{
    public long RunId { get; set; }
    public int Number { get; set; }
    public DateTimeOffset At { get; set; }

    /// <summary>"out" or "err" for a command's output, "job" for the runner's own lines.</summary>
    public required string Stream { get; set; }
    public required string Text { get; set; }
}

public static class JobsPersistence
{
    public const string Schema = "jobs";

    public static void Configure(ModelBuilder builder)
    {
        builder.Entity<DiscoveredFile>(entity =>
        {
            entity.ToTable("catalog_files", Schema);
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => item.ResourceId);
            entity.HasIndex(item => item.LocalPath).IsUnique();
            entity.HasIndex(item => new { item.Kind, item.ReferenceDate });
        });
        builder.Entity<JobRun>(entity =>
        {
            entity.ToTable("runs", Schema);
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => new { item.JobId, item.QueuedAt });
            entity.HasIndex(item => item.Status);
            // One active run per job.
            entity.HasIndex(item => item.JobId).IsUnique().HasFilter("status IN ('queued', 'running')").HasDatabaseName("ix_runs_active_job");
        });
        builder.Entity<JobRunLine>(entity =>
        {
            entity.ToTable("run_lines", Schema);
            entity.HasKey(item => new { item.RunId, item.Number });
            entity.HasOne<JobRun>().WithMany().HasForeignKey(item => item.RunId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
