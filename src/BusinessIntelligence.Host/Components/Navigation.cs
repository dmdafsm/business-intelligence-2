namespace BusinessIntelligence.Host.Components;

public sealed record NavigationDestination(string Page, string Label, string Icon);

public static class Navigation
{
    // M3 recommends three to five destinations; more are added as MVP steps deliver pages. The component reference at
    // /Design is for developers and has no link here.
    public static readonly IReadOnlyList<NavigationDestination> Destinations =
    [
        new("/Index", "Căutare", "search"),
        new("/Statistics", "Statistici", "bar-chart"),
    ];
}
