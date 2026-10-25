using System.Text.RegularExpressions;
using BusinessIntelligence.Tests.Search;
using Microsoft.Playwright;
using Xunit;

namespace BusinessIntelligence.BrowserTests;

public sealed class ProfilePageTests(SearchPortalFixture portal) : IClassFixture<SearchPortalFixture>
{
    private static readonly BrowserNewContextOptions NoScript = new() { JavaScriptEnabled = false, ViewportSize = new() { Width = 1280, Height = 900 } };
    private static readonly string[] Sections = ["Prezentare", "Contact", "Persoane", "Activități", "Istoric", "Financiar"];
    private static readonly BrowserNewContextOptions Phone = new() { ViewportSize = new() { Width = 360, Height = 740 }, IsMobile = true, HasTouch = true };

    [Fact]
    public Task SearchResultOpensAProfileWithEverySectionAndItsSource() =>
        portal.WithPageAsync(nameof(SearchResultOpensAProfileWithEverySectionAndItsSource), async page =>
        {
            await page.GotoAsync(portal.Address + "/?q=exemplu+soft");
            await page.GetByRole(AriaRole.Link, new() { Name = "EXEMPLU SOFT SRL" }).First.ClickAsync();
            await Assertions.Expect(page).ToHaveURLAsync(new Regex($"/firma/{SearchFixtures.ExempluSoft}$"));
            await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "EXEMPLU SOFT SRL", Level = 1 })).ToBeVisibleAsync();

            foreach (var section in Sections)
            {
                var region = page.GetByRole(AriaRole.Region, new() { Name = section });
                await Assertions.Expect(region).ToBeVisibleAsync();
                await Assertions.Expect(region.Locator(".md-source").First).ToContainTextAsync("Sursa:");
            }

            var contact = page.GetByRole(AriaRole.Region, new() { Name = "Contact" });
            await Assertions.Expect(contact).ToContainTextAsync("0264000002");
            await Assertions.Expect(contact).Not.ToContainTextAsync("sector 0");
            var people = page.GetByRole(AriaRole.Region, new() { Name = "Persoane" });
            await Assertions.Expect(people.GetByRole(AriaRole.Row, new() { Name = "POPESCU ANA" })).ToContainTextAsync("1980");
            await Assertions.Expect(people).Not.ToContainTextAsync("05.03.1980");
            await Assertions.Expect(people).Not.ToContainTextAsync("Turda");
            await Assertions.Expect(people.GetByRole(AriaRole.Row, new() { Name = "EXEMPLU INSOLV IPURL" })).ToContainTextAsync("Cluj-Napoca, Cluj, România");
            var history = page.GetByRole(AriaRole.Region, new() { Name = "Istoric" });
            await Assertions.Expect(history).ToContainTextAsync("VECHI NUME S.R.L.");
            await Assertions.Expect(history).ToContainTextAsync("J12/99/2000");
        }, NoScript);

    [Fact]
    public Task FinancialsShowEveryYearAndNeverTurnMissingIntoZero() =>
        portal.WithPageAsync(nameof(FinancialsShowEveryYearAndNeverTurnMissingIntoZero), async page =>
        {
            await page.GotoAsync(portal.Address + $"/firma/{SearchFixtures.ExempluSoft}");
            var financials = page.GetByRole(AriaRole.Region, new() { Name = "Financiar" });
            await Assertions.Expect(financials.GetByRole(AriaRole.Img)).ToHaveCountAsync(3);
            var table = financials.GetByRole(AriaRole.Table, new() { Name = "Indicatori principali" });
            await Assertions.Expect(table.GetByRole(AriaRole.Row)).ToHaveCountAsync(6);
            await Assertions.Expect(table.GetByRole(AriaRole.Row, new() { Name = "2011" })).ToContainTextAsync("nedepus");
            await Assertions.Expect(table.GetByRole(AriaRole.Row, new() { Name = "2014" })).ToContainTextAsync("nedepus");
            // 2013 filed with a blank turnover: labeled, not 0. 2012 filed 0 employees: shown as 0.
            await Assertions.Expect(table.GetByRole(AriaRole.Row, new() { Name = "2013" }).GetByRole(AriaRole.Cell).First).ToHaveTextAsync("— necompletat");
            await Assertions.Expect(table.GetByRole(AriaRole.Row, new() { Name = "2012" }).GetByRole(AriaRole.Cell).Nth(3)).ToHaveTextAsync("0");
            await Assertions.Expect(financials.Locator(".md-chart__bar--negative")).ToHaveCountAsync(1);
            await Assertions.Expect(financials).ToContainTextAsync("web2012.txt");
        }, NoScript);

    [Fact]
    public Task IndividualsWithoutCuiHideContactsAndUnknownCompaniesAreNotFound() =>
        portal.WithPageAsync(nameof(IndividualsWithoutCuiHideContactsAndUnknownCompaniesAreNotFound), async page =>
        {
            await page.GotoAsync(portal.Address + "/?q=popescu");
            await page.GetByRole(AriaRole.Link, new() { Name = "POPESCU ION PFA" }).ClickAsync();
            await Assertions.Expect(page).ToHaveURLAsync(new Regex(@"/firma\?nr=F12%2F5%2F2010$"));
            await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "Contact" })).ToContainTextAsync("nu se afișează pentru persoane fizice");
            await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "Financiar" })).ToContainTextAsync("Fără CUI valid");

            var missing = await page.GotoAsync(portal.Address + "/firma/" + SearchFixtures.Cui(123456));
            Assert.Equal(404, missing!.Status);
        }, NoScript);

    [Fact]
    public Task ProfileFitsAPhone() =>
        portal.WithPageAsync(nameof(ProfileFitsAPhone), async page =>
        {
            await page.GotoAsync(portal.Address + $"/firma/{SearchFixtures.ExempluSoft}");
            Assert.False(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > window.innerWidth"));
            await page.GetByRole(AriaRole.Link, new() { Name = "Financiar" }).ClickAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Financiar" })).ToBeInViewportAsync();
        }, Phone);
}
