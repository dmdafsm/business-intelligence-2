namespace BusinessIntelligence.Modules.Registry.Onrc;

/// <summary>
/// Reduces a registration's set of ONRC status codes to one lifecycle value. The most severe present code wins:
/// struck off, then insolvency, dissolution, suspension, active. Codes outside these lists (mentions such as an expired
/// office lease, or rare legal events) give Other only when nothing else applies; no status rows give Unknown.
/// </summary>
public static class Lifecycle
{
    public const string StruckOff = "StruckOff";
    public const string Insolvency = "Insolvency";
    public const string Dissolution = "Dissolution";
    public const string Suspended = "Suspended";
    public const string Active = "Active";
    public const string Other = "Other";
    public const string Unknown = "Unknown";

    /// <summary>Codes from N_STARE_FIRMA (19.10.2026), in order of precedence.</summary>
    public static readonly IReadOnlyList<(string Value, int[] Codes)> Rules =
    [
        (StruckOff, [1084]),
        // faliment, insolvență, reorganizare judiciară, insolvency laws 85/2006, 85/2014, 64/1995 and foreign insolvency
        (Insolvency, [1070, 1107, 1057, 1083, 1139, 1073, 1105, 1106, 1121]),
        // dizolvare (all legal grounds) and lichidare
        (Dissolution, [1049, 1052, 1076, 1094, 1098, 1100, 1109, 1113, 1120, 1133, 1145]),
        // întrerupere temporară and suspensions ordered under specific laws
        (Suspended, [1074, 1134, 1151, 1152, 1153]),
        (Active, [1048]),
    ];

    public static string FromCodes(IEnumerable<int> codes)
    {
        var present = codes.ToHashSet();
        if (present.Count == 0)
        {
            return Unknown;
        }

        foreach (var (value, ruleCodes) in Rules)
        {
            if (ruleCodes.Any(present.Contains))
            {
                return value;
            }
        }

        return Other;
    }

    /// <summary>The same rule as SQL over registry.registration_statuses rows aliased <c>s</c>.</summary>
    internal static string SqlCase()
    {
        var branches = Rules.Select(rule =>
            $"WHEN bool_or(s.status_code IN ({string.Join(", ", rule.Codes)})) THEN '{rule.Value}'");
        return $"CASE {string.Join(" ", branches)} ELSE '{Other}' END";
    }
}
