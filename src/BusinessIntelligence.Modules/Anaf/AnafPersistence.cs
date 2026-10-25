using Microsoft.EntityFrameworkCore;

namespace BusinessIntelligence.Modules.Anaf;

/// <summary>
/// One answer of the ANAF taxpayer web service (PlatitorTvaRest v9) for a CUI on a date. Only status fields are kept:
/// no address, phone, fax or bank account.
/// </summary>
public sealed class AnafStatus
{
    public long Cui { get; set; }

    /// <summary>The date the question was asked for (the service answers "as of" that date).</summary>
    public DateOnly CheckedOn { get; set; }

    /// <summary>False when ANAF did not find the CUI.</summary>
    public bool Found { get; set; }

    /// <summary>For example "INREGISTRAT din data 27.01.1993", as published.</summary>
    public string? RegistrationStatus { get; set; }
    public string? TaxOffice { get; set; }
    public string? CaenCode { get; set; }
    public bool? VatPayer { get; set; }

    /// <summary>The VAT registration periods as published (JSON array of start/end dates).</summary>
    public string? VatPeriods { get; set; }
    public bool? CashAccountingVat { get; set; }
    public DateOnly? CashAccountingVatSince { get; set; }
    public bool? SplitVat { get; set; }
    public bool? Inactive { get; set; }
    public DateOnly? InactiveSince { get; set; }
    public DateOnly? ReactivatedOn { get; set; }
    public DateOnly? DeregisteredOn { get; set; }
    public bool? EInvoice { get; set; }
    public DateOnly? EInvoiceSince { get; set; }
}

public static class AnafPersistence
{
    public const string Schema = "anaf";

    public static void Configure(ModelBuilder builder)
    {
        builder.Entity<AnafStatus>(entity =>
        {
            entity.ToTable("statuses", Schema);
            entity.HasKey(item => new { item.Cui, item.CheckedOn });
            entity.Property(item => item.VatPeriods).HasColumnType("jsonb");
        });
    }
}
