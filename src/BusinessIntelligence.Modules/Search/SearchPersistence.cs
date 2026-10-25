using Microsoft.EntityFrameworkCore;

namespace BusinessIntelligence.Modules.Search;

/// <summary>
/// A company of the published registry snapshot, as the search and grouping pages read it. Rebuilt by
/// <see cref="BuildSearchIndex"/>; owned by the Search module, which only reads the registry.
/// </summary>
public sealed class SearchCompany
{
    public required string CompanyKey { get; set; }
    public long? Cui { get; set; }
    public required string RegistrationNumber { get; set; }
    public string? Name { get; set; }

    /// <summary>The current name as compared in searches (see <see cref="SearchKeys.Name"/>); sorts the browse lists.</summary>
    public string? NameKey { get; set; }
    public string? LegalForm { get; set; }
    public required string Lifecycle { get; set; }
    public DateOnly? FirstRegistrationDate { get; set; }
    public short? CountyNumber { get; set; }
    public int? UatCode { get; set; }
    public int? LocalityCode { get; set; }

    /// <summary>CAEN version of the company's authorized activities; null when the registry lists none.</summary>
    public short? CaenVersion { get; set; }

    /// <summary>
    /// The authorized activities with every level above them, as "version:code" (3:J, 3:62, 3:620, 3:6201), so one
    /// containment test filters by section, division, group or class.
    /// </summary>
    public required string[] Caen { get; set; }
}

/// <summary>A name a company is found by: its current name and the other names its registrations had in earlier snapshots.</summary>
public sealed class SearchName
{
    public required string CompanyKey { get; set; }
    public required string NameKey { get; set; }
    public required string Name { get; set; }
    public bool Current { get; set; }

    /// <summary>For an earlier name, the last snapshot that still showed it.</summary>
    public DateOnly? LastSeen { get; set; }
}

/// <summary>A registry number of the company's registrations in the published snapshot, normalized by <see cref="SearchKeys.RegistrationNumber"/>.</summary>
public sealed class SearchRegistration
{
    public required string NumberKey { get; set; }
    public required string CompanyKey { get; set; }
}

/// <summary>
/// Precomputed company counts per county, lifecycle and CAEN code, so browsing without a name or locality filter
/// never counts millions of rows. County 0 means all counties and -1 an unknown county; an empty lifecycle or CAEN
/// token means any.
/// </summary>
public sealed class SearchCount
{
    public short CountyNumber { get; set; }
    public required string Lifecycle { get; set; }
    public required string Caen { get; set; }
    public int Companies { get; set; }
}

/// <summary>One build of the search tables; the newest is the one pages read.</summary>
public sealed class SearchBuild
{
    public int Id { get; set; }
    public int RegistrySnapshotId { get; set; }
    public DateTimeOffset BuiltAt { get; set; }
    public required string Report { get; set; }
}

public static class SearchPersistence
{
    public const string Schema = "search";

    public static void Configure(ModelBuilder builder)
    {
        builder.HasPostgresExtension("pg_trgm");
        builder.Entity<SearchCompany>(entity =>
        {
            entity.ToTable("companies", Schema);
            entity.HasKey(item => item.CompanyKey);
            entity.HasIndex(item => item.Cui);
            // Keys are folded to capitals and digits; the C collation lets one btree serve both prefix matches and name order.
            entity.Property(item => item.NameKey).UseCollation("C");
            entity.HasIndex(item => item.NameKey);
            entity.HasIndex(item => new { item.CountyNumber, item.UatCode });
            entity.HasIndex(item => item.Caen).HasMethod("gin");
        });
        builder.Entity<SearchName>(entity =>
        {
            entity.ToTable("names", Schema);
            entity.HasKey(item => new { item.CompanyKey, item.NameKey });
            entity.HasIndex(item => item.NameKey, "ix_names_name_key_trgm").HasMethod("gin").HasOperators("gin_trgm_ops");
            entity.Property(item => item.NameKey).UseCollation("C");
            entity.HasIndex(item => item.NameKey, "ix_names_name_key_prefix");
        });
        builder.Entity<SearchRegistration>(entity =>
        {
            entity.ToTable("registration_numbers", Schema);
            entity.HasKey(item => new { item.NumberKey, item.CompanyKey });
        });
        builder.Entity<SearchCount>(entity =>
        {
            entity.ToTable("counts", Schema);
            entity.HasKey(item => new { item.CountyNumber, item.Lifecycle, item.Caen });
        });
        builder.Entity<SearchBuild>(entity =>
        {
            entity.ToTable("builds", Schema);
            entity.Property(item => item.Report).HasColumnType("jsonb");
        });
    }
}
