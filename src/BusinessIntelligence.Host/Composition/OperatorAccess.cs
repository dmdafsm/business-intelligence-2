using System.Security.Cryptography;
using System.Text;
using BusinessIntelligence.Modules.Jobs;

namespace BusinessIntelligence.Host.Composition;

/// <summary>
/// The operator pages under /admin: one shared token (a Docker secret), checked once on a sign-in form and then kept
/// in an HTTP-only cookie limited to /admin. Without a configured token the pages do not exist.
/// </summary>
public sealed class OperatorAccess
{
    public const string Scheme = "Operator";
    public const string PathPrefix = "/admin";
    private const int MinimumTokenLength = 24;
    private readonly byte[]? _tokenHash;

    public OperatorAccess(string? token, string jobFile)
    {
        _tokenHash = token is { Length: >= MinimumTokenLength } ? SHA256.HashData(Encoding.UTF8.GetBytes(token)) : null;
        JobFile = jobFile;
    }

    public bool Enabled => _tokenHash is not null;

    public string JobFile { get; }

    public static async Task<OperatorAccess> CreateAsync(IConfiguration configuration)
    {
        var file = configuration["Admin:TokenFile"];
        var token = string.IsNullOrWhiteSpace(file) || !File.Exists(file) ? null : (await File.ReadAllTextAsync(file)).Trim();
        return new OperatorAccess(token, configuration["Jobs:File"] ?? JobsCommands.JobFile);
    }

    public bool Accepts(string? token) =>
        _tokenHash is not null && token is not null && CryptographicOperations.FixedTimeEquals(_tokenHash, SHA256.HashData(Encoding.UTF8.GetBytes(token.Trim())));

    /// <summary>The job file as it is now, read on each request so an edit shows at once.</summary>
    public Task<JobDefinitions> LoadJobsAsync(CancellationToken cancellationToken) => JobDefinitions.LoadAsync(JobFile, cancellationToken);
}
