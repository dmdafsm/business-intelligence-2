using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BusinessIntelligence.Host.Composition;

internal static class DatabaseCommands
{
    // Serializes concurrent migrate commands against the same database.
    private const long MigrationLock = 470202501;

    public static async Task<int> ExecuteAsync(string[] arguments, CancellationToken cancellationToken)
    {
        if (arguments is not ["database", "migrate", "--confirm-database", var name])
        {
            Console.Error.WriteLine("Commands: database migrate --confirm-database <name>");
            return 2;
        }

        try
        {
            var connection = await OperatorDatabase.ResolveConnectionAsync(cancellationToken);
            if (connection is null)
            {
                return 2;
            }

            // The operator names the target so a migration never runs against an unexpected database.
            if (string.IsNullOrWhiteSpace(name) || new NpgsqlConnectionStringBuilder(connection).Database != name)
            {
                Console.Error.WriteLine("The confirmed database name must match the configured target.");
                return 2;
            }

            await using var database = OperatorDatabase.Create(connection);
            await database.Database.OpenConnectionAsync(cancellationToken);
            await database.Database.ExecuteSqlAsync($"SELECT pg_advisory_lock({MigrationLock})", cancellationToken);
            try
            {
                await database.Database.MigrateAsync(cancellationToken);
            }
            finally
            {
                await database.Database.ExecuteSqlAsync($"SELECT pg_advisory_unlock({MigrationLock})", CancellationToken.None);
                await database.Database.CloseConnectionAsync();
            }

            Console.WriteLine("{\"migrated\":true}");
            return 0;
        }
        catch (DbException)
        {
            Console.Error.WriteLine("Database migration failed; check connectivity and operator permissions.");
            return 1;
        }
        catch (ArgumentException)
        {
            Console.Error.WriteLine("Invalid database configuration.");
            return 2;
        }
    }
}
