using Microsoft.EntityFrameworkCore;

namespace BusinessIntelligence.Modules.Funds;

/// <summary>
/// An EU-funded project from a published list: the Ministry of Investments and European Projects' lists of contracted
/// projects per operational programme, or AFIR's rural development (PNDR) projects. Only AFIR 2007–2013 gives the
/// beneficiary's tax code; the other lists give a name and a place, so a project is linked to a company on read, by
/// CUI, or by exact name within the county when exactly one company has that name there.
/// </summary>
public sealed class FundProject
{
    public int SourceFileId { get; set; }
    public int RowNumber { get; set; }
    public required string Programme { get; set; }
    public string? ProjectCode { get; set; }
    public string? Title { get; set; }

    /// <summary>Null when the beneficiary's identifier was a personal numeric code.</summary>
    public string? BeneficiaryName { get; set; }

    /// <summary>The beneficiary name as compared with company names (see <see cref="Search.SearchKeys.Name"/>).</summary>
    public string? BeneficiaryNameKey { get; set; }
    public long? BeneficiaryCui { get; set; }
    public string? County { get; set; }
    public short? CountyNumber { get; set; }
    public string? Locality { get; set; }
    public string? Status { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }

    /// <summary>Lei as published; the lists differ in which values they give.</summary>
    public decimal? EligibleValue { get; set; }
    public decimal? TotalValue { get; set; }
    public decimal? ContractedValue { get; set; }
    public decimal? PaidValue { get; set; }
}

public static class FundsPersistence
{
    public const string Schema = "funds";

    public static void Configure(ModelBuilder builder)
    {
        builder.Entity<FundProject>(entity =>
        {
            entity.ToTable("projects", Schema);
            entity.HasKey(item => new { item.SourceFileId, item.RowNumber });
            entity.HasIndex(item => item.BeneficiaryCui);
            entity.Property(item => item.BeneficiaryNameKey).UseCollation("C");
            entity.HasIndex(item => new { item.BeneficiaryNameKey, item.CountyNumber });
            foreach (var property in new[] { nameof(FundProject.EligibleValue), nameof(FundProject.TotalValue), nameof(FundProject.ContractedValue), nameof(FundProject.PaidValue) })
            {
                entity.Property(property).HasPrecision(20, 2);
            }
        });
    }
}
