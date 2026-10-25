using System.Text;
using Npgsql;

namespace BusinessIntelligence.Modules.Platform.Persistence;

public static class ApplicationDatabaseConnection
{
    public static async Task<string?> ResolveAsync(string? connectionString, string? passwordFile, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(connectionString) || string.IsNullOrWhiteSpace(passwordFile))
        {
            return connectionString;
        }

        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        if (!string.IsNullOrEmpty(builder.Password))
        {
            throw new ArgumentException("Configure the database password either inline or by file, not both.", nameof(passwordFile));
        }

        await using var stream = File.OpenRead(passwordFile);
        var buffer = new byte[1025];
        var length = await stream.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, cancellationToken);
        if (length > 1024)
        {
            throw new ArgumentException("Database password file exceeds the size limit.", nameof(passwordFile));
        }

        var password = Encoding.UTF8.GetString(buffer.AsSpan(0, length)).TrimEnd('\r', '\n');
        if (string.IsNullOrEmpty(password) || password.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException("Database password file must contain a nonempty password.", nameof(passwordFile));
        }

        builder.Password = password;
        return builder.ConnectionString;
    }
}
