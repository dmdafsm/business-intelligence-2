using BusinessIntelligence.Modules.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BusinessIntelligence.Host.Composition;

internal static class OperatorDatabase
{
    /// <summary>Operator commands use only the explicitly configured database; there is no default.</summary>
    public static async Task<string?> ResolveConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = await ApplicationDatabaseConnection.ResolveAsync(
            Environment.GetEnvironmentVariable("ConnectionStrings__Application"),
            Environment.GetEnvironmentVariable("Database__PasswordFile"), cancellationToken);
        if (string.IsNullOrWhiteSpace(connection))
        {
            await Console.Error.WriteLineAsync("Set ConnectionStrings__Application explicitly; no default database is used.");
        }

        return connection;
    }

    public static AppDbContext Create(string connection) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options);
}
