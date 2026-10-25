using Microsoft.EntityFrameworkCore;

namespace BusinessIntelligence.Modules.Financials;

/// <summary>
/// One company's annual statement as published by the Ministry of Finance. Amounts are lei as published; null means the
/// cell was blank, which is not the same as zero. Profit and loss are separate non-negative columns, as in the source.
/// </summary>
public sealed class FinancialStatement
{
    public short Year { get; set; }
    public long Cui { get; set; }

    /// <summary>BL (long/short/simplified balance sheet), UU (small entities) or IR (IFRS).</summary>
    public required string Family { get; set; }
    public string? CaenCode { get; set; }

    /// <summary>2 (Rev.2) for 2008–2024; null when the year mixes versions (2025).</summary>
    public short? CaenVersion { get; set; }
    public int SourceFileId { get; set; }

    public decimal? FixedAssets { get; set; }
    public decimal? CurrentAssets { get; set; }
    public decimal? Inventories { get; set; }
    public decimal? Receivables { get; set; }
    public decimal? Cash { get; set; }
    public decimal? PrepaidExpenses { get; set; }
    public decimal? Liabilities { get; set; }
    public decimal? DeferredIncome { get; set; }
    public decimal? Provisions { get; set; }
    public decimal? Equity { get; set; }
    public decimal? ShareCapital { get; set; }
    public decimal? RegiePatrimony { get; set; }
    public decimal? PublicPatrimony { get; set; }
    public decimal? Turnover { get; set; }
    public decimal? TotalIncome { get; set; }
    public decimal? TotalExpenses { get; set; }
    public decimal? GrossProfit { get; set; }
    public decimal? GrossLoss { get; set; }
    public decimal? NetProfit { get; set; }
    public decimal? NetLoss { get; set; }
    public int? Employees { get; set; }

    /// <summary>
    /// Quality notes set at import: <c>amount_out_of_range</c> (an amount of 1 trillion lei or more was stored as missing),
    /// <c>employees_out_of_range</c> (outside 0–1,000,000, stored as missing), <c>employees_unlikely</c> (kept as published,
    /// but 1,000+ employees with turnover under 1,000 lei each).
    /// </summary>
    public string[] QualityFlags { get; set; } = [];
}

public static class FinancialsPersistence
{
    public const string Schema = "financials";

    public static void Configure(ModelBuilder builder)
    {
        builder.Entity<FinancialStatement>(entity =>
        {
            entity.ToTable("statements", Schema);
            entity.HasKey(item => new { item.Cui, item.Year });
            entity.HasIndex(item => new { item.Year, item.Family });
            entity.Property(item => item.QualityFlags).HasDefaultValueSql("'{}'::text[]");
            foreach (var property in entity.Metadata.GetProperties().Where(property => property.ClrType == typeof(decimal?)))
            {
                property.SetPrecision(20);
                property.SetScale(0);
            }
        });
    }
}
