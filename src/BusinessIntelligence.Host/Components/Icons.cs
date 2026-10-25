namespace BusinessIntelligence.Host.Components;

/// <summary>Material Icons paths (Apache-2.0, see THIRD-PARTY-NOTICES.md) used by the inline sprite.</summary>
public static class Icons
{
    public static readonly IReadOnlyDictionary<string, string> Paths = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["search"] = "M15.5 14h-.79l-.28-.27A6.47 6.47 0 0 0 16 9.5 6.5 6.5 0 1 0 9.5 16c1.61 0 3.09-.59 4.23-1.57l.27.28v.79l5 4.99L20.49 19l-4.99-5zm-6 0C7.01 14 5 11.99 5 9.5S7.01 5 9.5 5 14 7.01 14 9.5 11.99 14 9.5 14z",
        ["home"] = "M10 20v-6h4v6h5v-8h3L12 3 2 12h3v8z",
        ["business"] = "M12 7V3H2v18h20V7H12zM6 19H4v-2h2v2zm0-4H4v-2h2v2zm0-4H4V9h2v2zm0-4H4V5h2v2zm4 12H8v-2h2v2zm0-4H8v-2h2v2zm0-4H8V9h2v2zm0-4H8V5h2v2zm10 12h-8v-2h2v-2h-2v-2h2v-2h-2V9h8v10zm-2-8h-2v2h2v-2zm0 4h-2v2h2v-2z",
        ["dashboard"] = "M3 13h8V3H3v10zm0 8h8v-6H3v6zm10 0h8V11h-8v10zm0-18v6h8V3h-8z",
        ["check"] = "M9 16.17 4.83 12l-1.42 1.41L9 19 21 7l-1.41-1.41z",
        ["arrow-drop-down"] = "M7 10l5 5 5-5z",
        ["chevron-right"] = "M10 6 8.59 7.41 13.17 12l-4.58 4.59L10 18l6-6z",
        ["info"] = "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 15h-2v-6h2v6zm0-8h-2V7h2v2z",
        ["close"] = "M19 6.41 17.59 5 12 10.59 6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 12 13.41 17.59 19 19 17.59 13.41 12z",
        ["chevron-left"] = "M15.41 7.41 14 6l-6 6 6 6 1.41-1.41L10.83 12z",
        ["filter-list"] = "M10 18h4v-2h-4v2zM3 6v2h18V6H3zm3 7h12v-2H6v2z",
        ["place"] = "M12 2C8.13 2 5 5.13 5 9c0 5.25 7 13 7 13s7-7.75 7-13c0-3.87-3.13-7-7-7zm0 9.5a2.5 2.5 0 0 1 0-5 2.5 2.5 0 0 1 0 5z",
    };
}
