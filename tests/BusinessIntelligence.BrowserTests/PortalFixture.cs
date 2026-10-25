using BusinessIntelligence.Host.Composition;
using BusinessIntelligence.Tests.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.Playwright;
using Xunit;

namespace BusinessIntelligence.BrowserTests;

public class PortalFixture : IAsyncLifetime
{
    private readonly PostgresFixture _postgres = new();
    private WebApplication? _app;
    private IPlaywright? _playwright;
    public IBrowser Browser { get; private set; } = null!;
    public string Address => _app!.Urls.Single();

    public async Task InitializeAsync()
    {
        await _postgres.InitializeAsync();
        await using (var database = _postgres.CreateContext())
        {
            await SeedAsync(database);
        }

        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "BusinessIntelligence.slnx")))
        {
            root = root.Parent;
        }

        _app = await PortalApplication.BuildAsync([
            "--environment", "Development", "--urls", "http://127.0.0.1:0",
            "--ConnectionStrings:Application", _postgres.ConnectionString,
            "--Logging:LogLevel:Default", "Warning"
        ], Path.Combine(root!.FullName, "src/BusinessIntelligence.Host"));
        await _app.StartAsync();
        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync(new() { Headless = true });
    }

    /// <summary>Runs once against the migrated database before the portal starts.</summary>
    protected virtual Task SeedAsync(BusinessIntelligence.Modules.Platform.Persistence.AppDbContext database) => Task.CompletedTask;

    public async Task WithPageAsync(string test, Func<IPage, Task> action, BrowserNewContextOptions? options = null)
    {
        await using var context = await Browser.NewContextAsync(options ?? new());
        var directory = Path.Combine(Environment.GetEnvironmentVariable("BROWSER_ARTIFACTS_DIR")
            ?? Path.Combine(Path.GetTempPath(), "bi-browser-evidence"), test);
        Directory.CreateDirectory(directory);
        await context.Tracing.StartAsync(new() { Screenshots = true, Snapshots = true, Sources = false });
        var page = await context.NewPageAsync();
        try
        {
            await action(page);
            await page.ScreenshotAsync(new() { Path = Path.Combine(directory, "result.png"), FullPage = true });
        }
        catch
        {
            try
            {
                await page.ScreenshotAsync(new() { Path = Path.Combine(directory, "failure.png"), FullPage = true });
                await context.Tracing.StopAsync(new() { Path = Path.Combine(directory, "trace.zip") });
            }
            catch (PlaywrightException)
            {
                // Browser failure must not mask the original assertion.
            }

            throw;
        }

        await context.Tracing.StopAsync(new() { Path = Path.Combine(directory, "trace.zip") });
    }

    public async Task DisposeAsync()
    {
        if (Browser is not null)
        {
            await Browser.DisposeAsync();
        }

        _playwright?.Dispose();
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }

        await _postgres.DisposeAsync();
    }
}
