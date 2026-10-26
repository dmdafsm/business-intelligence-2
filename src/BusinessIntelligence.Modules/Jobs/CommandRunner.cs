using System.Diagnostics;

namespace BusinessIntelligence.Modules.Jobs;

/// <summary>Runs one operator command and reports each output line; returns the exit code.</summary>
public interface ICommandRunner
{
    Task<int> RunAsync(IReadOnlyList<string> arguments, Func<string, string, Task> line, CancellationToken cancellationToken);
}

/// <summary>
/// Runs a command as a child process of the given program (the Host binary in production), with this process's
/// environment. Cancelling kills the child and its descendants.
/// </summary>
public sealed class ProcessCommandRunner(string program, IReadOnlyList<string> prefix) : ICommandRunner
{
    /// <summary>The current Host binary: <c>dotnet Host.dll</c> or the apphost itself.</summary>
    public static ProcessCommandRunner ForCurrentProcess()
    {
        var path = Environment.ProcessPath ?? throw new InvalidOperationException("The process path is unknown.");
        return Path.GetFileNameWithoutExtension(path).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? new ProcessCommandRunner(path, [Environment.GetCommandLineArgs()[0]])
            : new ProcessCommandRunner(path, []);
    }

    public async Task<int> RunAsync(IReadOnlyList<string> arguments, Func<string, string, Task> line, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(program)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in prefix.Concat(arguments))
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException($"{program} did not start.");
        // Lines from both streams go through one gate so the log keeps a single order.
        using var gate = new SemaphoreSlim(1, 1);
        async Task PumpAsync(StreamReader reader, string stream)
        {
            while (await reader.ReadLineAsync(CancellationToken.None) is { } text)
            {
                await gate.WaitAsync(CancellationToken.None);
                try
                {
                    await line(stream, text);
                }
                finally
                {
                    gate.Release();
                }
            }
        }

        var output = PumpAsync(process.StandardOutput, "out");
        var error = PumpAsync(process.StandardError, "err");
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(output, error);
            throw;
        }

        await Task.WhenAll(output, error);
        return process.ExitCode;
    }
}
