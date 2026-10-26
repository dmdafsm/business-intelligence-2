using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Xunit;

namespace BusinessIntelligence.BrowserTests;

public sealed class StatisticsPageTests(SearchPortalFixture portal) : IClassFixture<SearchPortalFixture>
{
    private static readonly BrowserNewContextOptions NoScript = new() { JavaScriptEnabled = false, ViewportSize = new() { Width = 1280, Height = 900 } };
    private static readonly BrowserNewContextOptions Phone = new() { ViewportSize = new() { Width = 360, Height = 740 }, IsMobile = true, HasTouch = true };

    [Fact]
    public Task FiltersLiveInTheUrlAndStateChangesAreNotDated() =>
        portal.WithPageAsync(nameof(FiltersLiveInTheUrlAndStateChangesAreNotDated), async page =>
        {
            await page.GotoAsync(portal.Address + "/");
            await page.GetByRole(AriaRole.Navigation).GetByRole(AriaRole.Link, new() { Name = "Statistici" }).ClickAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Statistici despre firme" })).ToBeVisibleAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Img)).ToHaveCountAsync(2);
            await Assertions.Expect(page.GetByRole(AriaRole.Row, new() { Name = "octombrie 2026 (parțial, până la 19.10.2026)" })).ToBeVisibleAsync();

            var changes = page.GetByRole(AriaRole.Table, new() { Name = "Schimbări de stare între instantanee" });
            await Assertions.Expect(changes.GetByRole(AriaRole.Row, new() { Name = "18.12.2024 și 08.12.2025" })).ToContainTextAsync("1");
            await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "Starea firmelor" })).ToContainTextAsync("ONRC nu publică data radierii");

            // Without JavaScript the filter form submits by GET, so the filters are in the URL.
            await page.GetByLabel("Județ", new() { Exact = true }).SelectOptionAsync(new SelectOptionValue { Label = "Cluj" });
            await page.GetByLabel("Formă", new() { Exact = true }).SelectOptionAsync(new SelectOptionValue { Label = "Societăți" });
            await page.GetByRole(AriaRole.Button, new() { Name = "Aplică" }).ClickAsync();
            await Assertions.Expect(page).ToHaveURLAsync(new Regex(@"/statistici\?judet=12&forma=societati$"));
            await Assertions.Expect(page.GetByText("— Cluj, Societăți.")).ToBeVisibleAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "Pe județe" })).ToHaveCountAsync(0);
            await page.GetByRole(AriaRole.Link, new() { Name = "Toate", Exact = true }).ClickAsync();
            await Assertions.Expect(page).ToHaveURLAsync(new Regex(@"/statistici$"));
        }, NoScript);

    [Fact]
    public Task StatisticsFitAPhone() =>
        portal.WithPageAsync(nameof(StatisticsFitAPhone), async page =>
        {
            await page.GotoAsync(portal.Address + "/statistici");
            await page.EvaluateAsync("document.querySelectorAll('details').forEach(details => details.open = true)");
            Assert.False(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > window.innerWidth"));
        }, Phone);
}
