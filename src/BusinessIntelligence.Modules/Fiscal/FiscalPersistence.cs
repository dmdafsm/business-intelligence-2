using Microsoft.EntityFrameworkCore;

namespace BusinessIntelligence.Modules.Fiscal;

/// <summary>
/// A stretch of MF identification releases in which a taxpayer's identification looked the same. A new period starts
/// when the name, type, place, street, phone, fax, registry reference, VAT, fiscal status or tax obligations change;
/// a type, unit type or registry reference that one release leaves blank is not a change.
/// Coverage differs by release (2021 lists a million taxpayers, later years leave out the struck off), so a taxpayer
/// missing from a release keeps its period; <see cref="Releases"/> counts the releases that actually listed it.
/// </summary>
public sealed class TaxpayerPeriod
{
    public long Cui { get; set; }
    public DateOnly ValidFrom { get; set; }

    /// <summary>The last release that listed the taxpayer like this.</summary>
    public DateOnly ValidTo { get; set; }

    /// <summary>True for the taxpayer's newest period, the one a later release can extend.</summary>
    public bool Latest { get; set; }
    public short Releases { get; set; }
    public string? Name { get; set; }
    public string? NameKey { get; set; }

    /// <summary>PJ (legal entity) or PF (individual); given from 2022, inferred before; null when unknown.</summary>
    public string? Kind { get; set; }
    public string? UnitType { get; set; }
    public long? ParentCui { get; set; }
    public string? County { get; set; }
    public string? Locality { get; set; }
    public string? Sector { get; set; }
    public string? PlaceKey { get; set; }

    // Street address, phone and fax are kept only for legal entities.
    public string? Street { get; set; }
    public string? StreetNumber { get; set; }
    public string? StreetKey { get; set; }
    public string? Block { get; set; }
    public string? Staircase { get; set; }
    public string? Floor { get; set; }
    public string? Apartment { get; set; }
    public string? AddressDetails { get; set; }
    public string? PostalCode { get; set; }
    public string? Phone { get; set; }
    public string? Fax { get; set; }
    public string? RegistrationNumber { get; set; }
    public string? AuthorizationAct { get; set; }

    /// <summary>Null when the release did not say.</summary>
    public bool? VatPayer { get; set; }
    public DateOnly? VatSince { get; set; }
    public string? FiscalStatus { get; set; }
    public DateOnly? StatusDate { get; set; }
    public DateOnly? DeregistrationDate { get; set; }
    public DateOnly? LastDeclaration { get; set; }
    public DateOnly? LastProcessed { get; set; }

    /// <summary>ANAF codes of the registered tax obligations (100 profit tax, 120 micro-enterprise tax, 602 payroll tax…).</summary>
    public required int[] Obligations { get; set; }

    /// <summary>Code to registration date, as JSON; only releases before 2022 give these dates.</summary>
    public string? ObligationDates { get; set; }
}

/// <summary>An MF identification release applied to the taxpayer periods, in date order.</summary>
public sealed class FiscalRelease
{
    public DateOnly ReleaseDate { get; set; }
    public DateTimeOffset ImportedAt { get; set; }
    public required string ParserVersion { get; set; }
    public required string Report { get; set; }
}

/// <summary>Taxpayer type from the releases that state it (2022 on); used to recognise individuals in older releases.</summary>
public sealed class TaxpayerKind
{
    public long Cui { get; set; }
    public required string Kind { get; set; }
    public DateOnly ReleaseDate { get; set; }
}

public static class FiscalPersistence
{
    public const string Schema = "fiscal";

    public static void Configure(ModelBuilder builder)
    {
        builder.Entity<TaxpayerPeriod>(entity =>
        {
            entity.ToTable("taxpayer_periods", Schema);
            entity.HasKey(item => new { item.Cui, item.ValidFrom });
            entity.Property(item => item.ObligationDates).HasColumnType("jsonb");
        });
        builder.Entity<FiscalRelease>(entity =>
        {
            entity.ToTable("releases", Schema);
            entity.HasKey(item => item.ReleaseDate);
            entity.Property(item => item.Report).HasColumnType("jsonb");
        });
        builder.Entity<TaxpayerKind>(entity =>
        {
            entity.ToTable("taxpayer_kinds", Schema);
            entity.HasKey(item => item.Cui);
            entity.Property(item => item.Cui).ValueGeneratedNever();
        });
    }
}
