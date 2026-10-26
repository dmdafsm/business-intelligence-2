namespace BusinessIntelligence.Modules.Financials;

/// <summary>
/// How a statement gets the CAEN class it is compared in. Years with a published version use it as published. For a year
/// that mixes Rev.2 and Rev.3 codes (2025), the declared code can mean its Rev.3 class (when Rev.3 has the code) or the
/// Rev.3 classes the INS correspondence table maps it to (when Rev.2 has it); a class is derived only when one candidate
/// remains, possibly after looking at the company's authorized activities in the registry.
/// </summary>
public static class StatementClasses
{
    /// <summary>Only one Rev.3 class can be meant.</summary>
    public const string OnlyCandidate = "single";

    /// <summary>Several were possible; exactly one is among the company's Rev.3 activities in the registry.</summary>
    public const string Registry = "registry";

    /// <summary>The company's registry activities are all still Rev.2, so the code is read as Rev.2, which maps to one class.</summary>
    public const string RegistryRev2 = "registry-rev2";

    /// <summary>
    /// Several stayed possible, but the declared code is itself a Rev.3 class among the company's registry activities and
    /// one of the classes its Rev.2 meaning splits into: the same class under either reading.
    /// </summary>
    public const string SameCode = "same-code";

    /// <summary>The class version and code a statement is compared in, or nulls when it has none.</summary>
    public static (short? Version, string? Code) Of(FinancialStatement statement) =>
        statement.CaenVersion is not null && statement.CaenCode is not null ? (statement.CaenVersion, statement.CaenCode)
        : (statement.ClassVersion, statement.ClassCode);

    /// <summary>SQL for the comparison class of statement alias <paramref name="alias"/>: version, then code.</summary>
    public static string VersionSql(string alias) => $"coalesce({alias}.caen_version, {alias}.class_version)";

    public static string CodeSql(string alias) => $"CASE WHEN {alias}.caen_version IS NOT NULL THEN {alias}.caen_code ELSE {alias}.class_code END";
}
