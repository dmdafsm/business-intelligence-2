using System.Diagnostics;
using BusinessIntelligence.Modules.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace BusinessIntelligence.Tests.Infrastructure;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly string _directory = Directory.CreateTempSubdirectory("bi-pg-").FullName;
    private string _binaryDirectory = string.Empty;

    public string ConnectionString { get; private set; } = string.Empty;
    public string BinaryDirectory => _binaryDirectory;

    public async Task InitializeAsync()
    {
        if (OperatingSystem.IsWindows())
        {
            throw new InvalidOperationException("The isolated PostgreSQL fixture requires Linux or macOS PostgreSQL server binaries.");
        }

        try
        {
            _binaryDirectory = Environment.GetEnvironmentVariable("BI_TEST_POSTGRES_BIN")
                ?? (await RunAsync("pg_config", ["--bindir"])).Trim();
            await RunAsync(Path.Combine(_binaryDirectory, "initdb"),
                ["--pgdata", Path.Combine(_directory, "data"), "--username=bi_test_owner", "--auth-local=trust",
                    "--auth-host=reject", "--encoding=UTF8", "--no-locale"]);
            var socket = _directory.Replace("'", "'\\''", StringComparison.Ordinal);
            await RunAsync(Path.Combine(_binaryDirectory, "pg_ctl"),
                ["--pgdata", Path.Combine(_directory, "data"), "--log", Path.Combine(_directory, "postgres.log"),
                    "--wait", "--timeout=20", "--options",
                    $"-c listen_addresses='' -c unix_socket_directories='{socket}' -c unix_socket_permissions=0700 -c fsync=off", "start"]);
            ConnectionString = new NpgsqlConnectionStringBuilder
            {
                Host = _directory,
                Database = "postgres",
                Username = "bi_test_owner",
                Pooling = false,
                Timeout = 5,
                CommandTimeout = 10,
                ApplicationName = "business-intelligence-tests"
            }.ConnectionString;
            await using var database = CreateContext();
            await database.Database.MigrateAsync();
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).Options);

    /// <summary>Creates and migrates a separate database in this cluster, so a test starts from an empty schema.</summary>
    public async Task<AppDbContext> CreateIsolatedContextAsync()
    {
        var name = "test_" + Guid.NewGuid().ToString("N");
        await using (var owner = CreateContext())
        {
            // The name is a generated GUID; CREATE DATABASE cannot take a parameter.
#pragma warning disable EF1003
            await owner.Database.ExecuteSqlRawAsync("CREATE DATABASE " + name);
#pragma warning restore EF1003
        }

        var connection = new NpgsqlConnectionStringBuilder(ConnectionString) { Database = name, CommandTimeout = 60 }.ConnectionString;
        var database = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options);
        await database.Database.MigrateAsync();
        return database;
    }

    public async Task DisposeAsync()
    {
        if (File.Exists(Path.Combine(_directory, "data", "postmaster.pid")))
        {
            await RunAsync(Path.Combine(_binaryDirectory, "pg_ctl"),
                ["--pgdata", Path.Combine(_directory, "data"), "--wait", "--timeout=20", "--mode=immediate", "stop"]);
        }

        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static async Task<string> RunAsync(string executable, string[] arguments)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start PostgreSQL fixture command.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var error = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new InvalidOperationException("PostgreSQL fixture command timed out.");
        }

        var text = await output;
        var diagnostic = await error;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"PostgreSQL fixture command {Path.GetFileName(executable)} failed: {diagnostic}");
        }

        return text;
    }
}
