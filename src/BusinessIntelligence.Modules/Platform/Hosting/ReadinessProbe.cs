namespace BusinessIntelligence.Modules.Platform.Hosting;

public static class ReadinessProbe
{
    private static readonly Uri Endpoint = new("http://127.0.0.1:8080/health/ready");

    public static async Task<bool> CheckAsync(HttpClient client, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client.GetAsync(Endpoint, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            return response.StatusCode == System.Net.HttpStatusCode.OK;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
