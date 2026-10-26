using System.Text.RegularExpressions;
using BusinessIntelligence.Modules.Platform.Persistence;
using BusinessIntelligence.Tests.Search;
using Microsoft.Playwright;
using Xunit;

namespace BusinessIntelligence.BrowserTests;

/// <summary>A portal over the synthetic search snapshot.</summary>
public sealed class SearchPortalFixture : PortalFixture
{
    protected override Task SeedAsync(AppDbContext database) => SearchFixtures.SeedAsync(database);
}

public sealed partial class SearchPageTests(SearchPortalFixture portal) : IClassFixture<SearchPortalFixture>
{
    private static readonly BrowserNewContextOptions NoScript = new() { JavaScriptEnabled = false, ViewportSize = new() { Width = 1280, Height = 900 } };
    private static readonly string[] PhonePaths = ["/", "/?q=exemplu", "/?judet=12&caen=62", "/Design"];
    private static readonly BrowserNewContextOptions Phone = new() { ViewportSize = new() { Width = 360, Height = 740 }, IsMobile = true, HasTouch = true };

    [Fact]
    public Task NameSearchRanksResultsAndFiltersNarrowThemWithoutJavaScript() =>
        portal.WithPageAsync(nameof(NameSearchRanksResultsAndFiltersNarrowThemWithoutJavaScript), async page =>
        {
            await page.GotoAsync(portal.Address + "/");
            await page.GetByRole(AriaRole.Searchbox).FillAsync("exemplu");
            await page.GetByRole(AriaRole.Button, new() { Name = "Caută" }).ClickAsync();

            var results = page.Locator(".result__name");
            await Assertions.Expect(page.GetByRole(AriaRole.Status)).ToHaveTextAsync("4 firme");
            await Assertions.Expect(results).ToHaveTextAsync(["EXEMPLU", "EXEMPLU SOFT SRL", "EXEMPLUL NOU SRL", "S.C. ALFA EXEMPLU S.R.L."]);

            var filters = page.GetByRole(AriaRole.Complementary, new() { Name = "Filtre" });
            await filters.GetByRole(AriaRole.Link, new() { Name = "Cluj" }).ClickAsync();
            await Assertions.Expect(page).ToHaveURLAsync(CountyClujUrl());
            await Assertions.Expect(page.GetByRole(AriaRole.Status)).ToHaveTextAsync("3 firme");
            await Assertions.Expect(filters.GetByRole(AriaRole.Heading, new() { Name = "Localitate" })).ToBeVisibleAsync();

            await filters.GetByRole(AriaRole.Link, new() { Name = "Radiată" }).ClickAsync();
            await Assertions.Expect(results).ToHaveTextAsync(["EXEMPLU", "EXEMPLUL NOU SRL"]);
            await Assertions.Expect(filters.GetByRole(AriaRole.Link, new() { Name = "Radiată" })).ToHaveAttributeAsync("aria-current", "true");

            // Removing the name keeps the other filters, which are now counted from the stored totals.
            await page.GetByRole(AriaRole.Link, new() { Name = "Elimină filtrul „exemplu”" }).ClickAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Status)).ToHaveTextAsync("2 firme");
            await Assertions.Expect(page.GetByRole(AriaRole.Searchbox)).ToHaveValueAsync("");
        }, NoScript);

    [Fact]
    public Task CaenGroupsDrillDownAndUnknownCodesAreNamed() =>
        portal.WithPageAsync(nameof(CaenGroupsDrillDownAndUnknownCodesAreNamed), async page =>
        {
            await page.GotoAsync(portal.Address + "/");
            await page.GetByRole(AriaRole.Link, new() { Name = "Telecomunicații și tehnologia informației" }).ClickAsync();
            var filters = page.GetByRole(AriaRole.Complementary, new() { Name = "Filtre" });
            await Assertions.Expect(page.GetByRole(AriaRole.Status)).ToHaveTextAsync("2 firme");
            await filters.GetByRole(AriaRole.Link, new() { Name = "Activități de programare" }).ClickAsync();
            await filters.GetByRole(AriaRole.Link, new() { Name = "Consultanță" }).ClickAsync();
            await Assertions.Expect(page.Locator(".result__name")).ToHaveTextAsync(["EXEMPLU SOFT SRL"]);
            await Assertions.Expect(filters.GetByRole(AriaRole.List, new() { Name = "Nivel CAEN selectat" })).ToContainTextAsync("622");

            await filters.GetByRole(AriaRole.Link, new() { Name = "Rev.2 (2008)" }).ClickAsync();
            await Assertions.Expect(page).Not.ToHaveURLAsync(new Regex("caen="));
            await Assertions.Expect(page.GetByRole(AriaRole.Status)).ToHaveTextAsync($"{SearchFixtures.Filler + 6} firme");

            await page.GotoAsync(portal.Address + "/?caen=6201");
            await Assertions.Expect(page.GetByRole(AriaRole.Note)).ToContainTextAsync("Codul CAEN 6201 nu există în Rev.3");
        }, NoScript);

    [Fact]
    public Task IdentifiersAndPaginationWork() =>
        portal.WithPageAsync(nameof(IdentifiersAndPaginationWork), async page =>
        {
            await page.GotoAsync(portal.Address + "/?q=" + Uri.EscapeDataString("RO " + SearchFixtures.ExempluSoft));
            await Assertions.Expect(page.Locator(".result__name")).ToHaveTextAsync(["EXEMPLU SOFT SRL"]);
            await page.GotoAsync(portal.Address + "/?q=" + Uri.EscapeDataString("vechi nume"));
            await Assertions.Expect(page.Locator(".result")).ToContainTextAsync("Denumire anterioară: VECHI NUME S.R.L.");

            await page.GotoAsync(portal.Address + "/?judet=40");
            var pages = page.GetByRole(AriaRole.Navigation, new() { Name = "Pagini" });
            await Assertions.Expect(pages).ToContainTextAsync("Pagina 1 din 2");
            await pages.GetByRole(AriaRole.Link, new() { Name = "Următoarea" }).ClickAsync();
            await Assertions.Expect(pages).ToContainTextAsync("Pagina 2 din 2");
            await Assertions.Expect(page.Locator(".result")).ToHaveCountAsync(SearchFixtures.Filler + 1 - 20);
        }, NoScript);

    [Fact]
    public Task FinancialFiltersSubmitWithoutJavaScriptAndKeepTheOtherFilters() =>
        portal.WithPageAsync(nameof(FinancialFiltersSubmitWithoutJavaScriptAndKeepTheOtherFilters), async page =>
        {
            await page.GotoAsync(portal.Address + $"/?judet={SearchFixtures.Cluj}");
            var form = page.GetByRole(AriaRole.Complementary, new() { Name = "Filtre" });
            await Assertions.Expect(form.GetByRole(AriaRole.Heading, new() { Name = "Date financiare 2013" })).ToBeVisibleAsync();
            await form.GetByRole(AriaRole.Group, new() { Name = "Număr mediu de salariați" }).GetByLabel("De la").FillAsync("1");
            await form.GetByLabel("Ordine").SelectOptionAsync(new SelectOptionValue { Label = "Salariați, descrescător" });
            await form.GetByRole(AriaRole.Button, new() { Name = "Aplică" }).ClickAsync();

            // A GET form also sends its empty fields; they are ignored.
            await Assertions.Expect(page).ToHaveURLAsync(new Regex($@"\?judet={SearchFixtures.Cluj}&.*sal_min=1&.*ordine=salariati$"));
            await Assertions.Expect(page.GetByRole(AriaRole.Status)).ToHaveTextAsync("1 firmă");
            await Assertions.Expect(page.GetByRole(AriaRole.Listitem).Filter(new() { HasText = "EXEMPLU SOFT SRL" }))
                .ToContainTextAsync("2013: cifra de afaceri necompletată · 3 salariați");

            // Thousands separators typed by a person keep their digits.
            await page.GotoAsync(portal.Address + "/?ca_min=1.000.000");
            await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Elimină filtrul CA ≥ 1.000.000 lei" })).ToBeVisibleAsync();
            await page.GetByRole(AriaRole.Link, new() { Name = "Elimină filtrul CA ≥ 1.000.000 lei" }).ClickAsync();
            await Assertions.Expect(page).ToHaveURLAsync(new Regex(@"/$"));
        }, NoScript);

    [Fact]
    public Task ResultsDownloadAsCsvWithTheSameFiltersAndOrder() =>
        portal.WithPageAsync(nameof(ResultsDownloadAsCsvWithTheSameFiltersAndOrder), async page =>
        {
            await page.GotoAsync(portal.Address + $"/?judet={SearchFixtures.Cluj}&ordine=salariati");
            var download = await page.RunAndWaitForDownloadAsync(() => page.GetByRole(AriaRole.Link, new() { Name = "CSV", Exact = true }).ClickAsync());
            Assert.Matches(@"^firme-\d{4}-\d{2}-\d{2}\.csv$", download.SuggestedFilename);
            var text = await File.ReadAllTextAsync(await download.PathAsync());
            var lines = text.Split("\r\n");
            Assert.StartsWith("CUI,Nr. înregistrare,Denumire", lines[0].TrimStart('\uFEFF'), StringComparison.Ordinal);
            // Ordered by employees, as on the page: EXEMPLU SOFT (3 employees in 2013) first.
            Assert.StartsWith($"{SearchFixtures.ExempluSoft},J2024000100123,EXEMPLU SOFT SRL,", lines[1], StringComparison.Ordinal);
            Assert.Contains(",2013,,3,10000,", lines[1], StringComparison.Ordinal);

            var workbook = await page.RunAndWaitForDownloadAsync(() => page.GetByRole(AriaRole.Link, new() { Name = "Descarcă Excel" }).ClickAsync());
            Assert.Matches(@"^firme-\d{4}-\d{2}-\d{2}\.xlsx$", workbook.SuggestedFilename);
            var sheet = BusinessIntelligence.Modules.Registry.Geography.SimpleXlsx.ReadFirstSheet(await File.ReadAllBytesAsync(await workbook.PathAsync()));
            Assert.Equal("EXEMPLU SOFT SRL", sheet[1][2]);
            Assert.Contains("Surse: Oficiul Național al Registrului Comerțului", text, StringComparison.Ordinal);

            // The bare home page has no list, so nothing to download.
            await page.GotoAsync(portal.Address + "/?handler=Csv");
            await Assertions.Expect(page).ToHaveURLAsync(new Regex(@"/$"));
        }, NoScript);

    [Fact]
    public Task LegalFormFilterAndRangeFieldsFitTheirColumn() =>
        portal.WithPageAsync(nameof(LegalFormFilterAndRangeFieldsFitTheirColumn), async page =>
        {
            await page.GotoAsync(portal.Address + $"/?judet={SearchFixtures.Cluj}");
            await page.GetByRole(AriaRole.Link, new() { Name = "PFA", Exact = true }).ClickAsync();
            await Assertions.Expect(page).ToHaveURLAsync(new Regex(@"forma=PFA"));
            await Assertions.Expect(page.GetByRole(AriaRole.Status)).ToHaveTextAsync("1 firmă");
            await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "POPESCU ION PFA" })).ToBeVisibleAsync();

            // The two bounds of a range sit side by side without overlapping.
            var range = page.GetByRole(AriaRole.Group, new() { Name = "Număr mediu de salariați" });
            var from = await range.GetByLabel("De la").BoundingBoxAsync();
            var to = await range.GetByLabel("Până la").BoundingBoxAsync();
            Assert.True(from!.X + from.Width <= to!.X, $"{from.X}+{from.Width} > {to.X}");
        }, NoScript);

    [Fact]
    public Task PhoneShowsResultsFirstWithoutHorizontalScroll() =>
        portal.WithPageAsync(nameof(PhoneShowsResultsFirstWithoutHorizontalScroll), async page =>
        {
            foreach (var path in PhonePaths)
            {
                await page.GotoAsync(portal.Address + path);
                Assert.False(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > window.innerWidth"), $"{path} scrolls horizontally at 360px");
            }

            await page.GotoAsync(portal.Address + "/?q=exemplu");
            var list = await page.Locator(".result-list").BoundingBoxAsync();
            var filters = await page.Locator("#filtre").BoundingBoxAsync();
            Assert.True(list!.Y < filters!.Y);
            await page.GetByRole(AriaRole.Link, new() { Name = "Filtre" }).ClickAsync();
            await Assertions.Expect(page).ToHaveURLAsync(new Regex("#filtre$"));
            await Assertions.Expect(page.Locator("#filtre")).ToBeInViewportAsync();
        }, Phone);

    [GeneratedRegex("judet=12")]
    private static partial Regex CountyClujUrl();
}
