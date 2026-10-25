namespace BusinessIntelligence.Host.Components;

/// <summary>One choice in a filter group: a link carrying the whole URL state with this value applied.</summary>
public sealed record FacetLink(string Label, string? Code, long Count, Dictionary<string, string> RouteValues, bool Selected);

/// <summary>A filter group; the first <paramref name="Visible"/> links show, the rest open under a disclosure (no script needed).</summary>
public sealed record FacetListModel(IReadOnlyList<FacetLink> Links, int Visible, string MoreLabel);
