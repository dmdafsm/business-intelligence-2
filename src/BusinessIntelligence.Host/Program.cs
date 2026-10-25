using BusinessIntelligence.Host.Composition;
using BusinessIntelligence.Modules.Platform.Hosting;

if (args is ["healthcheck"])
{
    using var handler = new HttpClientHandler { AllowAutoRedirect = false };
    using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(3) };
    return await ReadinessProbe.CheckAsync(client, CancellationToken.None) ? 0 : 1;
}

if (args is ["database" or "registry" or "financials", ..])
{
    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        cancellation.Cancel();
    };
    return args[0] switch
    {
        "registry" => await RegistryCommands.ExecuteAsync(args, cancellation.Token),
        "financials" => await FinancialCommands.ExecuteAsync(args, cancellation.Token),
        _ => await DatabaseCommands.ExecuteAsync(args, cancellation.Token),
    };
}

var app = await PortalApplication.BuildAsync(args);
await app.RunAsync();
return 0;
