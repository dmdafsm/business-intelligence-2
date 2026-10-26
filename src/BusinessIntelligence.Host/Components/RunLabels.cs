using System.Globalization;
using BusinessIntelligence.Modules.Jobs;

namespace BusinessIntelligence.Host.Components;

/// <summary>How the operator pages name run states, triggers and times.</summary>
public static class RunLabels
{
    private static readonly CultureInfo Romanian = CultureInfo.GetCultureInfo("ro-RO");

    public static string Status(string status) => status switch
    {
        RunStatus.Queued => "În coadă",
        RunStatus.Running => "Rulează",
        RunStatus.Succeeded => "Reușită",
        RunStatus.Failed => "Eșuată",
        RunStatus.Cancelled => "Anulată",
        _ => status,
    };

    /// <summary>The md-status modifier: error only for a failed run.</summary>
    public static string StatusClass(string status) => status switch
    {
        RunStatus.Succeeded => "md-status md-status--active",
        RunStatus.Failed => "md-status md-status--error",
        RunStatus.Running => "md-status md-status--suspended",
        _ => "md-status",
    };

    public static string Trigger(string trigger) => trigger switch
    {
        RunTrigger.Schedule => "programată",
        RunTrigger.Page => "din pagină",
        RunTrigger.Cli => "din linia de comandă",
        _ => trigger,
    };

    public static string Time(DateTimeOffset? value, TimeZoneInfo zone) =>
        value is { } time ? TimeZoneInfo.ConvertTime(time, zone).ToString("dd.MM.yyyy HH:mm", Romanian) : "—";

    public static string Duration(JobRun run, DateTimeOffset now)
    {
        if (run.StartedAt is not { } started)
        {
            return "—";
        }

        var elapsed = (run.FinishedAt ?? now) - started;
        return elapsed.TotalHours >= 1 ? $"{(int)elapsed.TotalHours} h {elapsed.Minutes} min"
            : elapsed.TotalMinutes >= 1 ? $"{(int)elapsed.TotalMinutes} min {elapsed.Seconds} s" : $"{elapsed.Seconds} s";
    }
}
