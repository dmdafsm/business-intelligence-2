using Microsoft.Playwright;
using Xunit;

namespace BusinessIntelligence.BrowserTests;

public sealed class AppShellTests(PortalFixture portal) : IClassFixture<PortalFixture>
{
    private static readonly BrowserNewContextOptions Desktop = new() { ViewportSize = new() { Width = 1280, Height = 800 } };
    private static readonly string[] DesignPaths = ["/Design", "/Design?tab=culori", "/Design?tab=tipografie"];
    private static readonly BrowserNewContextOptions Phone = new() { ViewportSize = new() { Width = 360, Height = 740 }, IsMobile = true, HasTouch = true };

    [Fact]
    public Task HomeWorksWithoutJavaScriptAndShowsAnHonestEmptyState() =>
        portal.WithPageAsync(nameof(HomeWorksWithoutJavaScriptAndShowsAnHonestEmptyState), async page =>
        {
            var response = await page.GotoAsync(portal.Address + "/");
            Assert.Equal(200, response!.Status);
            Assert.Contains("script-src 'self'", response.Headers["content-security-policy"], StringComparison.Ordinal);
            await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Caută o firmă" })).ToBeVisibleAsync();

            await page.GetByRole(AriaRole.Searchbox).FillAsync("  Exemplu SRL ");
            await page.GetByRole(AriaRole.Button, new() { Name = "Caută" }).ClickAsync();
            await Assertions.Expect(page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(@"\?q=\+*Exemplu"));
            await Assertions.Expect(page.GetByRole(AriaRole.Status)).ToContainTextAsync("Încă nu există date importate");
            await Assertions.Expect(page.GetByRole(AriaRole.Searchbox)).ToHaveValueAsync("Exemplu SRL");
        }, new() { JavaScriptEnabled = false, ViewportSize = Desktop.ViewportSize });

    [Fact]
    public Task ImportedLookingInputIsEscaped() =>
        portal.WithPageAsync(nameof(ImportedLookingInputIsEscaped), async page =>
        {
            await page.GotoAsync(portal.Address + "/?q=" + Uri.EscapeDataString("<img src=x onerror=alert(1)>"));
            Assert.Equal(0, await page.Locator("main img").CountAsync());
            await Assertions.Expect(page.GetByRole(AriaRole.Searchbox)).ToHaveValueAsync("<img src=x onerror=alert(1)>");
        });

    [Fact]
    public Task DesktopUsesTheNavigationRail() =>
        portal.WithPageAsync(nameof(DesktopUsesTheNavigationRail), async page =>
        {
            await page.GotoAsync(portal.Address + "/");
            var navigation = page.GetByRole(AriaRole.Navigation, new() { Name = "Navigare principală" });
            await Assertions.Expect(navigation).ToHaveCountAsync(1);
            await Assertions.Expect(navigation).ToHaveClassAsync("md-nav-rail");
            await Assertions.Expect(navigation.GetByRole(AriaRole.Link, new() { Name = "Căutare" })).ToHaveAttributeAsync("aria-current", "page");
        }, Desktop);

    [Fact]
    public Task PhoneUsesTheNavigationBarWithoutHorizontalScroll() =>
        portal.WithPageAsync(nameof(PhoneUsesTheNavigationBarWithoutHorizontalScroll), async page =>
        {
            foreach (var path in DesignPaths.Prepend("/"))
            {
                await page.GotoAsync(portal.Address + path);
                var navigation = page.GetByRole(AriaRole.Navigation, new() { Name = "Navigare principală" });
                await Assertions.Expect(navigation).ToHaveClassAsync("md-nav-bar");
                var overflow = await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > window.innerWidth");
                Assert.False(overflow, $"{path} scrolls horizontally at 360px");
            }
        }, Phone);

    [Fact]
    public Task SkipLinkMovesFocusToTheContent() =>
        portal.WithPageAsync(nameof(SkipLinkMovesFocusToTheContent), async page =>
        {
            await page.GotoAsync(portal.Address + "/");
            await page.Keyboard.PressAsync("Tab");
            var skip = page.GetByRole(AriaRole.Link, new() { Name = "Sari la conținut" });
            await Assertions.Expect(skip).ToBeFocusedAsync();
            await Assertions.Expect(skip).ToBeInViewportAsync();
            await page.Keyboard.PressAsync("Enter");
            await Assertions.Expect(page.Locator("main")).ToBeFocusedAsync();
        }, Desktop);

    [Fact]
    public async Task ColorSchemeFollowsTheSystemPreference()
    {
        string? light = null;
        string? dark = null;
        await portal.WithPageAsync(nameof(ColorSchemeFollowsTheSystemPreference) + "-light", async page =>
        {
            await page.GotoAsync(portal.Address + "/");
            light = await page.EvaluateAsync<string>("getComputedStyle(document.body).backgroundColor");
        }, new() { ColorScheme = ColorScheme.Light });
        await portal.WithPageAsync(nameof(ColorSchemeFollowsTheSystemPreference) + "-dark", async page =>
        {
            await page.GotoAsync(portal.Address + "/");
            dark = await page.EvaluateAsync<string>("getComputedStyle(document.body).backgroundColor");
        }, new() { ColorScheme = ColorScheme.Dark });

        Assert.Equal("rgb(254, 247, 255)", light);
        Assert.Equal("rgb(20, 18, 24)", dark);
    }

    [Fact]
    public Task PagesRaiseNoConsoleErrors() =>
        portal.WithPageAsync(nameof(PagesRaiseNoConsoleErrors), async page =>
        {
            var errors = new List<string>();
            page.Console += (_, message) =>
            {
                if (message.Type == "error")
                {
                    errors.Add(message.Text);
                }
            };
            foreach (var path in DesignPaths.Prepend("/?q=x").Prepend("/"))
            {
                await page.GotoAsync(portal.Address + path);
            }

            Assert.Empty(errors);
        }, Desktop);

    [Fact]
    public Task UnknownPageShowsTheNotFoundPage() =>
        portal.WithPageAsync(nameof(UnknownPageShowsTheNotFoundPage), async page =>
        {
            var response = await page.GotoAsync(portal.Address + "/nu-exista");
            Assert.Equal(404, response!.Status);
            await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Pagina nu există" })).ToBeVisibleAsync();
            await page.GetByRole(AriaRole.Link, new() { Name = "Înapoi la căutare" }).ClickAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Caută o firmă" })).ToBeVisibleAsync();
        });

    [Fact]
    public Task DesignReferenceSwitchesTabsByUrl() =>
        portal.WithPageAsync(nameof(DesignReferenceSwitchesTabsByUrl), async page =>
        {
            await page.GotoAsync(portal.Address + "/Design");
            await page.GetByRole(AriaRole.Link, new() { Name = "Culori" }).ClickAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Culori" })).ToHaveAttributeAsync("aria-current", "page");
            await Assertions.Expect(page.GetByText("primary-container")).ToBeVisibleAsync();
        }, new() { JavaScriptEnabled = false });
}
