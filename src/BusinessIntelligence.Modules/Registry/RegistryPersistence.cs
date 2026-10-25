using Microsoft.EntityFrameworkCore;

namespace BusinessIntelligence.Modules.Registry;

/// <summary>One imported source file. Pages show its dataset, publisher, license and reference date as attribution.</summary>
public sealed class SourceFile
{
    public int Id { get; set; }
    public required string CatalogId { get; set; }
    public required string Kind { get; set; }
    public required string Dataset { get; set; }
    public required string FileName { get; set; }
    public required string Url { get; set; }
    public required string Publisher { get; set; }
    public required string License { get; set; }
    public DateOnly ReferenceDate { get; set; }
    public required string Sha256 { get; set; }
    public long Bytes { get; set; }
    public required string ParserVersion { get; set; }
    public DateTimeOffset ImportedAt { get; set; }

    /// <summary>Counts from the import (rows read, rejected, merged…), kept as JSON for the task notes and diagnostics.</summary>
    public required string Report { get; set; }
}

/// <summary>Which file currently feeds a dataset. Switching it publishes a new snapshot atomically.</summary>
public sealed class PublishedDataset
{
    public required string Kind { get; set; }
    public int SourceFileId { get; set; }
    public DateTimeOffset PublishedAt { get; set; }
}

public sealed class CaenEntry
{
    public short Version { get; set; }
    public required string Code { get; set; }
    public required string Level { get; set; }
    public required string Section { get; set; }
    public string? Division { get; set; }
    public string? Group { get; set; }

    /// <summary>Null when the source row has no name (seen in the 1998 version).</summary>
    public string? Name { get; set; }
}

public sealed class CompanyStatusCode
{
    public int Code { get; set; }
    public required string Name { get; set; }
}

public sealed class SirutaPlace
{
    public int Code { get; set; }
    public required string Name { get; set; }
    public string? PostalCode { get; set; }
    public short CountyNumber { get; set; }
    public int ParentCode { get; set; }
    public short Type { get; set; }
    public short Level { get; set; }
    public short Environment { get; set; }
}

/// <summary>One registry number in a snapshot, merged from all source rows that carry it.</summary>
public sealed class Registration
{
    public int SnapshotId { get; set; }
    public required string RegistrationNumber { get; set; }
    public string? Euid { get; set; }
    public string? Name { get; set; }
    public long? Cui { get; set; }
    public required string CuiState { get; set; }
    public string? LegalForm { get; set; }
    public required string FormCategory { get; set; }
    public DateOnly? RegistrationDate { get; set; }
    public string? Country { get; set; }
    public string? County { get; set; }
    public string? Locality { get; set; }
    public string? Street { get; set; }
    public string? StreetNumber { get; set; }
    public string? Block { get; set; }
    public string? Staircase { get; set; }
    public string? Floor { get; set; }
    public string? Apartment { get; set; }
    public string? PostalCode { get; set; }
    public string? Sector { get; set; }
    public string? AddressDetails { get; set; }
    public string? Website { get; set; }
    public string? ParentCountry { get; set; }
    public short? CountyNumber { get; set; }
    public int? UatCode { get; set; }
    public int? LocalityCode { get; set; }
    public required string GeographyState { get; set; }
    public short SourceRows { get; set; }
    public required string[] ConflictingFields { get; set; }

    /// <summary>Summary of the current ONRC status codes (see <see cref="Onrc.Lifecycle"/>); Unknown until statuses are imported.</summary>
    public string Lifecycle { get; set; } = Onrc.Lifecycle.Unknown;
}

/// <summary>One current ONRC status code of a registration. The source has no dates: this is a set, not a history.</summary>
public sealed class RegistrationStatus
{
    public int SnapshotId { get; set; }
    public required string RegistrationNumber { get; set; }
    public int StatusCode { get; set; }
}

/// <summary>An authorized CAEN activity of a company's current registration, in that registration's newest CAEN version.</summary>
public sealed class CompanyActivity
{
    public int SnapshotId { get; set; }
    public required string CompanyKey { get; set; }
    public short CaenVersion { get; set; }
    public required string CaenCode { get; set; }
}

/// <summary>A company in a snapshot: all registrations with the same valid CUI, or a single registration without one.</summary>
public sealed class Company
{
    public int SnapshotId { get; set; }

    /// <summary>"C" + CUI, or "R" + registry number when the company has no valid CUI.</summary>
    public required string CompanyKey { get; set; }
    public long? Cui { get; set; }
    public required string RegistrationNumber { get; set; }
    public string? Name { get; set; }
    public string? LegalForm { get; set; }
    public required string FormCategory { get; set; }
    public DateOnly? RegistrationDate { get; set; }
    public DateOnly? FirstRegistrationDate { get; set; }
    public short RegistrationCount { get; set; }
    public short? CountyNumber { get; set; }
    public int? UatCode { get; set; }
    public int? LocalityCode { get; set; }
    public required string GeographyState { get; set; }
    public string? Website { get; set; }
    public string Lifecycle { get; set; } = Onrc.Lifecycle.Unknown;

    /// <summary>CAEN version of the activities in company_activities; null when the source lists none.</summary>
    public short? CaenVersion { get; set; }
}

/// <summary>
/// A stretch of consecutive yearly snapshots in which a registration looked the same (name, CUI, place, lifecycle).
/// A new period starts when one of them changes. Missing from a snapshot is not a change: the period just stops being
/// extended, and absence is never read as being struck off.
/// </summary>
public sealed class RegistrationPeriod
{
    public required string RegistrationNumber { get; set; }
    public DateOnly ValidFrom { get; set; }

    /// <summary>The last snapshot in which the registration still looked like this.</summary>
    public DateOnly ValidTo { get; set; }
    public string? Name { get; set; }

    /// <summary>The name as compared between snapshots (folded, legal-form spellings unified).</summary>
    public string? NameKey { get; set; }
    public long? Cui { get; set; }
    public required string CuiState { get; set; }
    public string? County { get; set; }
    public string? Locality { get; set; }
    public string? Address { get; set; }
    public short? CountyNumber { get; set; }
    public int? UatCode { get; set; }
    public int? LocalityCode { get; set; }
    public required string Lifecycle { get; set; }
    public required int[] StatusCodes { get; set; }
}

/// <summary>A registry snapshot applied to the history, in date order.</summary>
public sealed class HistorySnapshot
{
    public DateOnly SnapshotDate { get; set; }
    public DateTimeOffset ImportedAt { get; set; }
    public required string ParserVersion { get; set; }
    public required string Report { get; set; }
}

public static class RegistryPersistence
{
    public const string Schema = "registry";

    public static void Configure(ModelBuilder builder)
    {
        builder.Entity<SourceFile>(entity =>
        {
            entity.ToTable("source_files", Schema);
            entity.HasIndex(file => new { file.Sha256, file.ParserVersion }).IsUnique();
            entity.Property(file => file.Sha256).HasMaxLength(64).IsFixedLength();
            entity.Property(file => file.Report).HasColumnType("jsonb");
        });
        builder.Entity<PublishedDataset>(entity =>
        {
            entity.ToTable("published_datasets", Schema);
            entity.HasKey(item => item.Kind);
            entity.HasOne<SourceFile>().WithMany().HasForeignKey(item => item.SourceFileId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<CaenEntry>(entity =>
        {
            entity.ToTable("caen", Schema);
            entity.HasKey(item => new { item.Version, item.Code });
        });
        builder.Entity<CompanyStatusCode>(entity =>
        {
            entity.ToTable("status_codes", Schema);
            entity.HasKey(item => item.Code);
            entity.Property(item => item.Code).ValueGeneratedNever();
        });
        builder.Entity<SirutaPlace>(entity =>
        {
            entity.ToTable("siruta", Schema);
            entity.HasKey(item => item.Code);
            entity.Property(item => item.Code).ValueGeneratedNever();
            entity.HasIndex(item => item.ParentCode);
        });
        builder.Entity<Registration>(entity =>
        {
            entity.ToTable("registrations", Schema);
            entity.HasKey(item => new { item.SnapshotId, item.RegistrationNumber });
            entity.HasIndex(item => new { item.SnapshotId, item.Cui });
            entity.Property(item => item.Lifecycle).HasDefaultValue(Onrc.Lifecycle.Unknown);
        });
        builder.Entity<RegistrationStatus>(entity =>
        {
            entity.ToTable("registration_statuses", Schema);
            entity.HasKey(item => new { item.SnapshotId, item.RegistrationNumber, item.StatusCode });
        });
        builder.Entity<CompanyActivity>(entity =>
        {
            entity.ToTable("company_activities", Schema);
            entity.HasKey(item => new { item.SnapshotId, item.CompanyKey, item.CaenVersion, item.CaenCode });
            entity.HasIndex(item => new { item.SnapshotId, item.CaenVersion, item.CaenCode });
        });
        builder.Entity<RegistrationPeriod>(entity =>
        {
            entity.ToTable("registration_periods", Schema);
            entity.HasKey(item => new { item.RegistrationNumber, item.ValidFrom });
            entity.HasIndex(item => item.Cui);
        });
        builder.Entity<HistorySnapshot>(entity =>
        {
            entity.ToTable("history_snapshots", Schema);
            entity.HasKey(item => item.SnapshotDate);
            entity.Property(item => item.Report).HasColumnType("jsonb");
        });
        builder.Entity<Company>(entity =>
        {
            entity.ToTable("companies", Schema);
            entity.HasKey(item => new { item.SnapshotId, item.CompanyKey });
            entity.HasIndex(item => new { item.SnapshotId, item.Cui });
            entity.HasIndex(item => new { item.SnapshotId, item.RegistrationNumber });
            entity.Property(item => item.Lifecycle).HasDefaultValue(Onrc.Lifecycle.Unknown);
        });
    }
}
