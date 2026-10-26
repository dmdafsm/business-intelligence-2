using BusinessIntelligence.Modules.Jobs;
using BusinessIntelligence.Modules.Platform.Persistence;
using Microsoft.Playwright;
using Xunit;

namespace BusinessIntelligence.BrowserTests;

/// <summary>A portal with an operator token, a small job file and one finished run.</summary>
public sealed class AdminPortalFixture : PortalFixture
{
    public const string Token = "synthetic-operator-token-for-browser-tests";
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("bi-admin-");

    protected override IEnumerable<string> PortalArguments
    {
        get
        {
            var token = Path.Combine(_directory.FullName, "admin-token");
            File.WriteAllText(token, Token + "\n");
            var jobs = Path.Combine(_directory.FullName, "import-jobs.json");
            File.WriteAllText(jobs, """
                {
                  "timeZone": "Europe/Bucharest",
                  "allowedHosts": ["data.gov.ro"],
                  "jobs": [
                    { "id": "seap", "title": "Achiziții publice (SEAP)", "schedule": "0 9 * * 3", "commands": [["procurement", "import-all", "{raw}"]] },
                    { "id": "anaf-status", "title": "Starea fiscală ANAF", "schedule": "0 1 3 * *", "commands": [["anaf", "harvest"]] }
                  ]
                }
                """);
            return ["--Admin:TokenFile", token, "--Jobs:File", jobs];
        }
    }

    protected override async Task SeedAsync(AppDbContext database)
    {
        var at = new DateTimeOffset(2026, 10, 21, 7, 0, 0, TimeSpan.Zero);
        var run = new JobRun
        {
            JobId = "seap",
            Trigger = RunTrigger.Schedule,
            Status = RunStatus.Failed,
            QueuedAt = at,
            StartedAt = at,
            FinishedAt = at.AddMinutes(3),
            Step = "procurement import-all /data/raw",
            Error = "procurement import-all /data/raw exited with 1.",
        };
        database.JobRuns.Add(run);
        await database.SaveChangesAsync();
        database.JobRunLines.AddRange(
            new JobRunLine { RunId = run.Id, Number = 1, At = at, Stream = "job", Text = "RUN: procurement import-all /data/raw" },
            new JobRunLine { RunId = run.Id, Number = 2, At = at, Stream = "err", Text = "Import refused: <b>the header</b> does not match." });
        database.DiscoveredFiles.Add(new DiscoveredFile
        {
            Id = "seap-contracts-2026-q4",
            Kind = "seap-contracts",
            Dataset = "Achiziții publice 2026",
            FileName = "contracte-t_iv_2026.xlsx",
            Url = "https://data.gov.ro/dataset/x/resource/y/download/contracte-t_iv_2026.xlsx",
            Publisher = "ADR",
            License = "CC BY 4.0",
            ReferenceDate = new DateOnly(2026, 12, 31),
            Bytes = 15_000_000,
            Sha256 = new string('a', 64),
            Year = 2026,
            Family = "Q4",
            LocalPath = "seap-history/2026/contracte-t_iv_2026.xlsx",
            Grouping = string.Empty,
            JobId = "seap",
            RunId = run.Id,
            ResourceId = "y",
            DiscoveredAt = at,
        });
        await database.SaveChangesAsync();
    }
}

public sealed class AdminPageTests(AdminPortalFixture portal) : IClassFixture<AdminPortalFixture>
{
    private static readonly BrowserNewContextOptions NoScript = new() { JavaScriptEnabled = false, ViewportSize = new() { Width = 1280, Height = 800 } };
    private static readonly string[] Pages = ["/admin/intrare", "/admin/joburi/rulare/1", "/admin/joburi"];
    private static readonly BrowserNewContextOptions Phone = new() { ViewportSize = new() { Width = 360, Height = 740 }, IsMobile = true, HasTouch = true };

    private async Task SignInAsync(IPage page, string path = "/admin/joburi")
    {
        await page.GotoAsync(portal.Address + path);
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Intrare operator" })).ToBeVisibleAsync();
        await page.GetByLabel("Token de operator").FillAsync(AdminPortalFixture.Token);
        await page.GetByRole(AriaRole.Button, new() { Name = "Intră" }).ClickAsync();
    }

    [Fact]
    public Task TheOperatorSignsInQueuesARunAndFollowsItWithoutJavaScript() =>
        portal.WithPageAsync(nameof(TheOperatorSignsInQueuesARunAndFollowsItWithoutJavaScript), async page =>
        {
            await page.GotoAsync(portal.Address + "/admin/joburi");
            await Assertions.Expect(page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/admin/intrare\\?ReturnUrl="));
            await page.GetByLabel("Token de operator").FillAsync("wrong-token-wrong-token-wrong");
            await page.GetByRole(AriaRole.Button, new() { Name = "Intră" }).ClickAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Alert)).ToHaveTextAsync("Token incorect.");

            await page.GetByLabel("Token de operator").FillAsync(AdminPortalFixture.Token);
            await page.GetByRole(AriaRole.Button, new() { Name = "Intră" }).ClickAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Joburi de import" })).ToBeVisibleAsync();
            var seap = page.GetByRole(AriaRole.Row).Filter(new() { HasText = "Achiziții publice (SEAP)" });
            await Assertions.Expect(seap).ToContainTextAsync("0 9 * * 3");
            await Assertions.Expect(seap).ToContainTextAsync("Eșuată");
            await Assertions.Expect(page.GetByRole(AriaRole.Row).Filter(new() { HasText = "seap-contracts-2026-q4" })).ToContainTextAsync("31.12.2026");

            var anaf = page.GetByRole(AriaRole.Row).Filter(new() { HasText = "Starea fiscală ANAF" });
            await anaf.GetByRole(AriaRole.Button, new() { Name = "Pornește" }).ClickAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { NameRegex = new System.Text.RegularExpressions.Regex("^Rularea \\d+: anaf-status$") })).ToBeVisibleAsync();
            await Assertions.Expect(page.Locator(".md-status")).ToHaveTextAsync("În coadă");
            await Assertions.Expect(page.Locator("meta[http-equiv=refresh]")).ToHaveCountAsync(1);
            Assert.Contains("no-store", (await page.ReloadAsync())!.Headers["cache-control"], StringComparison.Ordinal);

            // Starting it again keeps the one queued run.
            await page.GetByRole(AriaRole.Link, new() { Name = "Joburi de import" }).ClickAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Row).Filter(new() { HasText = "Starea fiscală ANAF" }).GetByRole(AriaRole.Button, new() { Name = "Pornește" })).ToBeDisabledAsync();

            await page.GetByRole(AriaRole.Link, new() { Name = "În coadă", Exact = true }).ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Anulează" }).ClickAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Status)).ToHaveTextAsync("Anularea a fost cerută.");
            await Assertions.Expect(page.Locator(".md-status")).ToHaveTextAsync("Anulată");
            await Assertions.Expect(page.Locator("meta[http-equiv=refresh]")).ToHaveCountAsync(0);

            await page.GetByRole(AriaRole.Button, new() { Name = "Ieșire" }).ClickAsync();
            await page.GotoAsync(portal.Address + "/admin/joburi");
            await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Intrare operator" })).ToBeVisibleAsync();
        }, NoScript);

    [Fact]
    public Task AFailedRunShowsItsErrorAndEscapedLog() =>
        portal.WithPageAsync(nameof(AFailedRunShowsItsErrorAndEscapedLog), async page =>
        {
            await SignInAsync(page, "/admin/joburi/rulare/1");
            await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Rularea 1: seap" })).ToBeVisibleAsync();
            await Assertions.Expect(page.Locator(".md-status")).ToHaveTextAsync("Eșuată");
            await Assertions.Expect(page.Locator(".md-facts")).ToContainTextAsync("exited with 1.");
            await Assertions.Expect(page.Locator(".md-log")).ToContainTextAsync("Import refused: <b>the header</b> does not match.");
            Assert.Equal(0, await page.Locator(".md-log b").CountAsync());
            Assert.Equal(0, await page.GetByRole(AriaRole.Button, new() { Name = "Anulează" }).CountAsync());

            var download = await page.RunAndWaitForDownloadAsync(() => page.GetByRole(AriaRole.Link, new() { Name = "Descarcă tot jurnalul" }).ClickAsync());
            Assert.Equal("rulare-1.txt", download.SuggestedFilename);
        });

    [Fact]
    public Task OperatorPagesFitAPhone() =>
        portal.WithPageAsync(nameof(OperatorPagesFitAPhone), async page =>
        {
            await SignInAsync(page);
            foreach (var path in Pages)
            {
                await page.GotoAsync(portal.Address + path);
                var overflow = await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > window.innerWidth");
                Assert.False(overflow, $"{path} scrolls horizontally at 360px");
            }
        }, Phone);
}
