namespace BusinessIntelligence.Host.Components;

public sealed record NavigationDestination(string Page, string Label, string Icon, bool DevelopmentOnly = false);

public static class Navigation
{
    // M3 recommends three to five destinations; more are added as MVP steps deliver pages.
    public static readonly IReadOnlyList<NavigationDestination> Destinations =
    [
        new("/Index", "Căutare", "search"),
        new("/Design", "Design", "dashboard", DevelopmentOnly: true),
    ];
}
