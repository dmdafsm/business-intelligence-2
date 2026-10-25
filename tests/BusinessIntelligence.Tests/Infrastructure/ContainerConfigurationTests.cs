using System.Net;
using BusinessIntelligence.Modules.Platform.Hosting;
using BusinessIntelligence.Modules.Platform.Persistence;
using Npgsql;
using Xunit;

namespace BusinessIntelligence.Tests.Infrastructure;

public sealed class ContainerConfigurationTests
{
    [Fact]
    public async Task PasswordFileIsReadWithoutTreatingItsContentAsConnectionParameters()
    {
        var directory = Directory.CreateTempSubdirectory("bi-secret-test-");
        try
        {
            var path = Path.Combine(directory.FullName, "password");
            await File.WriteAllTextAsync(path, "synthetic;Database=wrong-target\n");
            var resolved = await ApplicationDatabaseConnection.ResolveAsync("Host=localhost;Database=expected-target", path, CancellationToken.None);
            var parsed = new NpgsqlConnectionStringBuilder(resolved);
            Assert.Equal("expected-target", parsed.Database);
            Assert.Equal("synthetic;Database=wrong-target", parsed.Password);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n")]
    [InlineData("invalid\0value")]
    public async Task EmptyAndInvalidSecretsAreRejected(string content)
    {
        var directory = Directory.CreateTempSubdirectory("bi-secret-test-");
        try
        {
            var path = Path.Combine(directory.FullName, "password");
            await File.WriteAllTextAsync(path, content);
            await Assert.ThrowsAsync<ArgumentException>(() => ApplicationDatabaseConnection.ResolveAsync("Host=localhost", path, CancellationToken.None));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task OversizedSecretsAndAmbiguousPasswordConfigurationAreRejected()
    {
        var directory = Directory.CreateTempSubdirectory("bi-secret-test-");
        try
        {
            var path = Path.Combine(directory.FullName, "password");
            await File.WriteAllTextAsync(path, new string('x', 1025));
            await Assert.ThrowsAsync<ArgumentException>(() => ApplicationDatabaseConnection.ResolveAsync("Host=localhost", path, CancellationToken.None));
            await Assert.ThrowsAsync<ArgumentException>(() => ApplicationDatabaseConnection.ResolveAsync("Host=localhost;Password=synthetic-inline-value", path, CancellationToken.None));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ExistingEnvironmentConnectionWithoutFileStillWorks()
    {
        const string connection = "Host=localhost;Database=existing-target";
        Assert.Equal(connection, await ApplicationDatabaseConnection.ResolveAsync(connection, null, CancellationToken.None));
        Assert.Null(await ApplicationDatabaseConnection.ResolveAsync(null, null, CancellationToken.None));
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, false)]
    [InlineData(HttpStatusCode.InternalServerError, false)]
    [InlineData(HttpStatusCode.Redirect, false)]
    public async Task ContainerProbeRequiresAnOkReadinessResponse(HttpStatusCode status, bool expected)
    {
        using var handler = new StatusHandler(status);
        using var client = new HttpClient(handler);
        Assert.Equal(expected, await ReadinessProbe.CheckAsync(client, CancellationToken.None));
    }

    private sealed class StatusHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("http://127.0.0.1:8080/health/ready", request.RequestUri?.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }
}
