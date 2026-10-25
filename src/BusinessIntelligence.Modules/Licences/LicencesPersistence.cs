using Microsoft.EntityFrameworkCore;

namespace BusinessIntelligence.Modules.Licences;

/// <summary>
/// A licence, authorisation or contract a regulator lists for a holder: ANRSC public utility licences (valid, withdrawn,
/// rejected), ANRM mining licences, CNAS contracts with health care providers. Linked to a company by CUI, or, for ANRM
/// which lists only names, by exact name when no other company has it.
/// </summary>
public sealed class Licence
{
    public int SourceFileId { get; set; }
    public int RowNumber { get; set; }

    /// <summary>ANRSC, ANRM or CNAS.</summary>
    public required string Authority { get; set; }

    /// <summary>For example "valabilă", "retrasă", "respinsă", "activă", "contract".</summary>
    public required string Status { get; set; }
    public string? HolderName { get; set; }
    public string? HolderNameKey { get; set; }
    public long? HolderCui { get; set; }

    /// <summary>The service, activity, substances or provider type, as published.</summary>
    public string? Activity { get; set; }

    /// <summary>Licence number, class, validity, order, contract type or perimeter, as published.</summary>
    public string? Details { get; set; }
    public string? Place { get; set; }
}

public static class LicencesPersistence
{
    public const string Schema = "licences";

    public static void Configure(ModelBuilder builder)
    {
        builder.Entity<Licence>(entity =>
        {
            entity.ToTable("licences", Schema);
            entity.HasKey(item => new { item.SourceFileId, item.RowNumber });
            entity.HasIndex(item => item.HolderCui);
            entity.Property(item => item.HolderNameKey).UseCollation("C");
            entity.HasIndex(item => item.HolderNameKey);
        });
    }
}
