using BusinessIntelligence.Host.Composition;
using BusinessIntelligence.Modules.Platform.Hosting;

if (args is ["healthcheck", "worker"])
{
    return JobsCommands.WorkerAlive() ? 0 : 1;
}

if (args is ["healthcheck"])
{
    using var handler = new HttpClientHandler { AllowAutoRedirect = false };
    using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(3) };
    return await ReadinessProbe.CheckAsync(client, CancellationToken.None) ? 0 : 1;
}

if (args is ["database" or "registry" or "financials" or "fiscal" or "search" or "procurement" or "anaf" or "funds" or "licences" or "jobs" or "market" or "risk", ..])
{
    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        cancellation.Cancel();
    };
    if (args[0] != "database")
    {
        await JobsCommands.IncludeDiscoveredFilesAsync(cancellation.Token);
    }

    return args[0] switch
    {
        "jobs" => await JobsCommands.ExecuteAsync(args, cancellation.Token),
        "registry" => await RegistryCommands.ExecuteAsync(args, cancellation.Token),
        "financials" => await FinancialCommands.ExecuteAsync(args, cancellation.Token),
        "fiscal" => await FiscalCommands.ExecuteAsync(args, cancellation.Token),
        "search" => await SearchCommands.ExecuteAsync(args, cancellation.Token),
        "procurement" or "funds" or "licences" => await ProcurementCommands.ExecuteAsync(args, cancellation.Token),
        "anaf" => await AnafCommands.ExecuteAsync(args, cancellation.Token),
        "market" => await MarketCommands.ExecuteAsync(args, cancellation.Token),
        "risk" => await RiskCommands.ExecuteAsync(args, cancellation.Token),
        _ => await DatabaseCommands.ExecuteAsync(args, cancellation.Token),
    };
}

var app = await PortalApplication.BuildAsync(args);
await app.RunAsync();
return 0;
