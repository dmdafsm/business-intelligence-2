namespace BusinessIntelligence.Modules.Registry.Onrc;

/// <summary>
/// The SQL that keys a representative for linking across registrations (ADR 0004). Names and places are folded:
/// Romanian diacritics removed, capitals, and everything but letters and digits dropped, so "Nicoleta-Sofia" and
/// "NICOLETA SOFIA" match. Natural persons need a birth date and a place of birth; an unreadable birth date gets no key.
/// </summary>
public static class RepresentativeKeys
{
    /// <summary>The key of the row aliased <paramref name="alias"/> (columns of <c>registry.representatives</c>).</summary>
    public static string Sql(string alias) => $"""
        CASE WHEN {Fold(alias + ".name")} = '' THEN NULL
             WHEN {alias}.birth_date IS NOT NULL THEN
                 CASE WHEN {Fold(alias + ".birth_locality")} <> '' THEN 'N:' || {Fold(alias + ".name")} || ':' || {alias}.birth_date::text || ':' || {Fold(alias + ".birth_locality")} END
             WHEN {alias}.birth_date_text IS NOT NULL THEN NULL
             WHEN coalesce({alias}.locality, {alias}.county, {alias}.country) IS NOT NULL THEN 'L:' || {Fold(alias + ".name")}
        END
        """;

    private static string Fold(string column) =>
        $"regexp_replace(upper(translate(coalesce({column}, ''), 'ăâîșşțţĂÂÎȘŞȚŢ', 'aaissttAAISSTT')), '[^A-Z0-9]', '', 'g')";
}
