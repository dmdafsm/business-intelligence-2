using Microsoft.EntityFrameworkCore;

namespace BusinessIntelligence.Modules.Procurement;

/// <summary>
/// One row of a SEAP export: a winner of a contract, or a direct purchase. A contract with several winners has one row
/// per winner, all with the contract's full value: the source does not say how the value is shared.
/// </summary>
public sealed class ProcurementAward
{
    public int SourceFileId { get; set; }

    /// <summary>Sheet row number in the source file.</summary>
    public int RowNumber { get; set; }

    /// <summary>"contract" or "direct" (direct purchase).</summary>
    public required string Kind { get; set; }
    public short Year { get; set; }
    public short Quarter { get; set; }
    public string? AuthorityName { get; set; }
    public long? AuthorityCui { get; set; }

    /// <summary>Award notice number for contracts, purchase number for direct purchases.</summary>
    public string? Reference { get; set; }
    public string? Procedure { get; set; }
    public string? ContractType { get; set; }
    public string? CpvCode { get; set; }
    public string? CpvName { get; set; }

    /// <summary>The purchase description; only direct purchases publish one.</summary>
    public string? Title { get; set; }
    public string? Lot { get; set; }
    public string? ContractNumber { get; set; }
    public DateOnly? PublishedOn { get; set; }

    /// <summary>Contract date for contracts, completion date for direct purchases.</summary>
    public DateOnly? ContractDate { get; set; }

    /// <summary>Lei as published; null when the cell is blank or unreadable.</summary>
    public decimal? ValueRon { get; set; }

    /// <summary>For direct purchases: financed from EU funds (Da/Nu); null when not stated.</summary>
    public bool? EuFunded { get; set; }
    public string? Programme { get; set; }

    /// <summary>Null when the winner's identifier looked like a personal numeric code: then the name is withheld too.</summary>
    public string? WinnerName { get; set; }
    public long? WinnerCui { get; set; }

    /// <summary>Valid, Missing, InvalidChecksum, NonNumeric, PersonalCode… (see <see cref="Registry.CuiState"/>).</summary>
    public required string WinnerCuiState { get; set; }

    /// <summary>Winners of the same contract in this file (1 for direct purchases).</summary>
    public short Winners { get; set; } = 1;

    /// <summary>True on one row per contract (the first winner listed), so totals by authority count each contract once.</summary>
    public bool FirstWinner { get; set; } = true;
}

public static class ProcurementPersistence
{
    public const string Schema = "procurement";

    public static void Configure(ModelBuilder builder)
    {
        builder.Entity<ProcurementAward>(entity =>
        {
            entity.ToTable("awards", Schema);
            entity.HasKey(item => new { item.SourceFileId, item.RowNumber });
            entity.HasIndex(item => item.WinnerCui);
            entity.HasIndex(item => item.AuthorityCui);
            entity.Property(item => item.FirstWinner).HasDefaultValue(true);
            entity.Property(item => item.ValueRon).HasPrecision(20, 2);
        });
    }
}
