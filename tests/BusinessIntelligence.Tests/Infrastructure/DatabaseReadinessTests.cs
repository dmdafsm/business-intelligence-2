using BusinessIntelligence.Modules.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace BusinessIntelligence.Tests.Infrastructure;

public sealed class DatabaseReadinessTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task MigratedDatabaseIsReady()
    {
        await using var database = postgres.CreateContext();
        Assert.True(await DatabaseReadiness.IsReadyAsync(database, CancellationToken.None));
    }

    [Fact]
    public async Task UnmigratedDatabaseIsNotReady()
    {
        await using (var owner = postgres.CreateContext())
        {
            await owner.Database.ExecuteSqlRawAsync("DROP DATABASE IF EXISTS readiness_empty");
            await owner.Database.ExecuteSqlRawAsync("CREATE DATABASE readiness_empty");
        }

        var connection = new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { Database = "readiness_empty" }.ConnectionString;
        await using var database = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options);
        Assert.False(await DatabaseReadiness.IsReadyAsync(database, CancellationToken.None));
    }

    [Fact]
    public async Task UnreachableDatabaseIsNotReady()
    {
        var connection = new NpgsqlConnectionStringBuilder(postgres.ConnectionString)
        {
            Host = Path.Combine(Path.GetTempPath(), "bi-missing-socket-" + Guid.NewGuid().ToString("N")),
            Timeout = 1
        }.ConnectionString;
        await using var database = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options);
        Assert.False(await DatabaseReadiness.IsReadyAsync(database, CancellationToken.None));
    }
}
