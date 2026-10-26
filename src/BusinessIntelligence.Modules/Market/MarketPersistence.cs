using Microsoft.EntityFrameworkCore;

namespace BusinessIntelligence.Modules.Market;

/// <summary>
/// Registry entities by month of first registration, county, kind and current state, from the published ONRC snapshot.
/// County 0 means the registry gives no county.
/// </summary>
public sealed class MonthlyRegistrations
{
    public DateOnly Month { get; set; }
    public short CountyNumber { get; set; }
    public required string FormCategory { get; set; }
    public required string Lifecycle { get; set; }
    public int Companies { get; set; }
}

/// <summary>
/// Registrations whose state differs from the one they had in the previous yearly snapshot. ONRC publishes no event dates,
/// so the change happened at some point after <see cref="PreviousSnapshot"/> and on or before <see cref="ObservedOn"/>.
/// </summary>
public sealed class LifecycleChange
{
    public DateOnly ObservedOn { get; set; }
    public DateOnly PreviousSnapshot { get; set; }
    public short CountyNumber { get; set; }
    public required string FormCategory { get; set; }
    public required string FromLifecycle { get; set; }
    public required string ToLifecycle { get; set; }
    public int Registrations { get; set; }
}

/// <summary>One build of the market tables; the newest is the one the page reads.</summary>
public sealed class MarketBuild
{
    public int Id { get; set; }
    public int RegistrySnapshotId { get; set; }

    /// <summary>The date of the ONRC snapshot the build describes.</summary>
    public DateOnly ReferenceDate { get; set; }
    public DateTimeOffset BuiltAt { get; set; }
}

public static class MarketPersistence
{
    public const string Schema = "market";

    public static void Configure(ModelBuilder builder)
    {
        builder.Entity<MonthlyRegistrations>(entity =>
        {
            entity.ToTable("registrations", Schema);
            entity.HasKey(item => new { item.Month, item.CountyNumber, item.FormCategory, item.Lifecycle });
        });
        builder.Entity<LifecycleChange>(entity =>
        {
            entity.ToTable("lifecycle_changes", Schema);
            entity.HasKey(item => new { item.ObservedOn, item.CountyNumber, item.FormCategory, item.FromLifecycle, item.ToLifecycle });
        });
        builder.Entity<MarketBuild>(entity => entity.ToTable("builds", Schema));
    }
}
