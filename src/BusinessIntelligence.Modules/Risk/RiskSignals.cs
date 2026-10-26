using Microsoft.EntityFrameworkCore;

namespace BusinessIntelligence.Modules.Risk;

/// <summary>
/// One warning signal of one company, with what it refers to: a financial <see cref="Year"/>, an ANAF answer date
/// (<see cref="Since"/> and <see cref="CheckedOn"/>), an <see cref="Amount"/> or a ratio and its cohort threshold.
/// </summary>
public sealed class RiskSignal
{
    public required string CompanyKey { get; set; }
    public long? Cui { get; set; }
    public required string Signal { get; set; }
    public short? Year { get; set; }
    public decimal? Amount { get; set; }

    /// <summary>For <c>high_debt_ratio</c>: the company's ratio and the cohort's P90, with the CAEN class compared.</summary>
    public decimal? Ratio { get; set; }
    public decimal? Threshold { get; set; }
    public string? CaenCode { get; set; }
    public DateOnly? Since { get; set; }
    public DateOnly? CheckedOn { get; set; }
}

public sealed record SignalDefinition(string Key, string Slug, string Name, string Rule, string Source);

public static class RiskSignals
{
    public const string Version = "signals-v1";
    public const string AnafInactive = "anaf_inactive";
    public const string NoRecentStatement = "no_recent_statement";
    public const string NegativeEquity = "negative_equity";
    public const string RepeatedLosses = "repeated_losses";
    public const string HighDebtRatio = "high_debt_ratio";

    /// <summary>The search filter for companies with none of the signals.</summary>
    public const string NoneSlug = "niciunul";

    public static readonly IReadOnlyList<SignalDefinition> All =
    [
        new(AnafInactive, "inactiv", "Inactiv fiscal", "ANAF o declară contribuabil inactiv în ultimul răspuns al serviciului web.", "ANAF"),
        new(NoRecentStatement, "nedepus", "Situații financiare nedepuse",
            "Firmă activă, înregistrată înainte de ultimul an financiar publicat, fără situații financiare publicate pentru acel an și cel dinainte.", "Ministerul Finanțelor"),
        new(NegativeEquity, "capital-negativ", "Capitaluri proprii negative", "Capitaluri proprii sub zero în ultimul an financiar publicat.", "Ministerul Finanțelor"),
        new(RepeatedLosses, "pierderi", "Pierderi trei ani la rând", "Pierdere netă în fiecare dintre ultimii trei ani financiari publicați.", "Ministerul Finanțelor"),
        new(HighDebtRatio, "indatorare", "Îndatorare ridicată",
            "Datorii / (active imobilizate + active circulante) peste percentila 90 a clasei CAEN, în ultimul an cu comparație.", "Ministerul Finanțelor"),
    ];

    public static SignalDefinition? BySlug(string? slug) => All.FirstOrDefault(item => item.Slug == slug);

    public static SignalDefinition Get(string key) => All.Single(item => item.Key == key);
}

/// <summary>When the signals were built, from which years, under which rules version.</summary>
public sealed class RiskBuild
{
    public int Id { get; set; }
    public short? FinancialYear { get; set; }
    public short? BenchmarkYear { get; set; }
    public required string Version { get; set; }
    public DateTimeOffset BuiltAt { get; set; }
}

public static class RiskPersistence
{
    public const string Schema = "risk";

    public static void Configure(ModelBuilder builder)
    {
        builder.Entity<RiskSignal>(entity =>
        {
            entity.ToTable("signals", Schema);
            entity.HasKey(item => new { item.CompanyKey, item.Signal });
            entity.HasIndex(item => new { item.Signal, item.CompanyKey });
            entity.HasIndex(item => item.Cui);
            entity.Property(item => item.Signal).HasMaxLength(32);
            entity.Property(item => item.Amount).HasPrecision(20, 0);
            entity.Property(item => item.Ratio).HasPrecision(24, 6);
            entity.Property(item => item.Threshold).HasPrecision(24, 6);
        });
        builder.Entity<RiskBuild>(entity =>
        {
            entity.ToTable("builds", Schema);
            entity.Property(item => item.Version).HasMaxLength(32);
        });
    }
}
