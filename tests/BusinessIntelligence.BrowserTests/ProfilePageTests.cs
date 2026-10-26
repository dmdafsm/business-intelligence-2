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
            var procurement = page.GetByRole(AriaRole.Region, new() { Name = "Achiziții publice" });
            await Assertions.Expect(procurement).ToContainTextAsync("2025 (T1)");
            await Assertions.Expect(procurement.GetByRole(AriaRole.Row, new() { Name = "SCOALA GIMNAZIALA TEST" })).ToContainTextAsync("4.999,50");
            await Assertions.Expect(procurement.Locator(".md-source")).ToContainTextAsync("Autoritatea pentru Digitalizarea României");
            var anaf = page.GetByRole(AriaRole.Region, new() { Name = "Situația la ANAF" });
            await Assertions.Expect(anaf).ToContainTextAsync("mare contribuabil");
            await Assertions.Expect(anaf).ToContainTextAsync("Da, din 01.07.2022");
            await Assertions.Expect(page.GetByRole(AriaRole.Img, new() { Name = "Harta României, cu județul evidențiat" })).ToBeVisibleAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "Fonduri europene" })).ToContainTextAsync("Digitalizarea EXEMPLU");
            await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "Licențe și contracte cu autorități" })).ToContainTextAsync("Farmacie");
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
    public Task DerivedIndicatorsAndTheCaenComparisonExplainWhatIsMissing() =>
        portal.WithPageAsync(nameof(DerivedIndicatorsAndTheCaenComparisonExplainWhatIsMissing), async page =>
        {
            await page.GotoAsync(portal.Address + $"/firma/{SearchFixtures.ExempluSoft}");
            var financials = page.GetByRole(AriaRole.Region, new() { Name = "Financiar" });
            var derived = financials.GetByRole(AriaRole.Table, new() { Name = "Indicatori calculați" });
            // 2010: 120,000 profit on 1,000,000 turnover; 2012 has no 2011 statement to grow from.
            await Assertions.Expect(derived.GetByRole(AriaRole.Row, new() { Name = "Marja netă" })).ToContainTextAsync("12,0%");
            await Assertions.Expect(derived.GetByRole(AriaRole.Row, new() { Name = "Creșterea cifrei de afaceri" }))
                .ToContainTextAsync("nicio situație depusă pentru anul anterior");

            var comparison = financials.GetByRole(AriaRole.Table, new() { Name = "Comparație cu firmele din aceeași clasă CAEN, 2013" });
            var equity = comparison.GetByRole(AriaRole.Row, new() { Name = "Rentabilitatea capitalurilor proprii" });
            await Assertions.Expect(equity).ToContainTextAsync("20,0%");
            await Assertions.Expect(equity).ToContainTextAsync("în sfertul de sus");
            await Assertions.Expect(equity).ToContainTextAsync("40 firme, toate mărimile");
            var margin = comparison.GetByRole(AriaRole.Row, new() { Name = "Marja netă" });
            await Assertions.Expect(margin).ToContainTextAsync("cifra de afaceri necompletată");
            await Assertions.Expect(margin).ToContainTextAsync("prea puține firme pentru o comparație");
            await Assertions.Expect(financials).ToContainTextAsync("CAEN Rev.2 6201");
        }, NoScript);

    [Fact]
    public Task SignalsShowTheirReasonAndFilterTheSearch() =>
        portal.WithPageAsync(nameof(SignalsShowTheirReasonAndFilterTheSearch), async page =>
        {
            await page.GotoAsync(portal.Address + $"/firma/{SearchFixtures.ExempluSoft}");
            var none = page.GetByRole(AriaRole.Region, new() { Name = "Semnale de atenție" });
            await Assertions.Expect(none).ToContainTextAsync("Niciun semnal din datele verificate.");
            await Assertions.Expect(none).ToContainTextAsync("nu un rating");

            // ALFA EXEMPLU is active, registered in 2010 and has no statement for 2012 or 2013.
            await page.GotoAsync(portal.Address + $"/?judet={SearchFixtures.Bucuresti}");
            await page.GetByRole(AriaRole.Link, new() { Name = "Situații financiare nedepuse" }).ClickAsync();
            await Assertions.Expect(page).ToHaveURLAsync(new Regex(@"semnal=nedepus"));
            await page.GetByRole(AriaRole.Link, new() { Name = "S.C. ALFA EXEMPLU S.R.L." }).ClickAsync();
            var signals = page.GetByRole(AriaRole.Region, new() { Name = "Semnale de atenție" });
            await Assertions.Expect(signals).ToContainTextAsync("Situații financiare nedepuse");
            await Assertions.Expect(signals).ToContainTextAsync("Nicio situație financiară publicată pentru 2012 și 2013. Sursa: Ministerul Finanțelor.");
        }, NoScript);

    [Fact]
    public Task RepresentativesLinkToTheirOtherCompaniesWithAWarning() =>
        portal.WithPageAsync(nameof(RepresentativesLinkToTheirOtherCompaniesWithAWarning), async page =>
        {
            await page.GotoAsync(portal.Address + $"/firma/{SearchFixtures.ExempluSoft}");
            var people = page.GetByRole(AriaRole.Region, new() { Name = "Persoane" });
            var ana = people.GetByRole(AriaRole.Row).Filter(new() { HasText = "POPESCU ANA" });
            await ana.GetByText("1 firmă").ClickAsync();
            await ana.GetByRole(AriaRole.Link, new() { Name = "S.C. ALFA EXEMPLU S.R.L." }).ClickAsync();
            await Assertions.Expect(page).ToHaveURLAsync(new Regex($"/firma/{SearchFixtures.AlfaExemplu}$"));

            await page.GoBackAsync();
            await Assertions.Expect(people).ToContainTextAsync("legătura poate uni persoane diferite cu aceleași date");
            // Only the birth year is shown, never the full date or the place of birth.
            await Assertions.Expect(people).Not.ToContainTextAsync("05.03.1980");
            await Assertions.Expect(people).Not.ToContainTextAsync("Turda");
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

            // An opened wide table scrolls inside its own region, never the page, and only sideways.
            await page.EvaluateAsync("document.querySelectorAll('details').forEach(details => details.open = true)");
            Assert.False(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > window.innerWidth"));
            Assert.False(await page.EvaluateAsync<bool>(
                "[...document.querySelectorAll('.md-table-wrapper')].some(w => getComputedStyle(w).overflowY !== 'hidden' || w.clientWidth > document.documentElement.clientWidth)"));

            // Scrolled to the last year, the indicator names stay at the left edge of their table.
            Assert.True(await page.EvaluateAsync<bool>("""
                (() => {
                    const wrapper = document.querySelector('.md-data-table--sticky-first').closest('.md-table-wrapper');
                    const label = wrapper.querySelector('tbody th');
                    wrapper.scrollLeft = wrapper.scrollWidth;
                    return wrapper.scrollLeft > 0 && Math.abs(label.getBoundingClientRect().left - wrapper.getBoundingClientRect().left) < 2;
                })()
                """));
            await page.GetByRole(AriaRole.Link, new() { Name = "Financiar" }).ClickAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Financiar" })).ToBeInViewportAsync();
        }, Phone);
}
