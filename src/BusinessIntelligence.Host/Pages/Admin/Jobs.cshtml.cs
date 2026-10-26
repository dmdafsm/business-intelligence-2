using System.Text.Json;
using BusinessIntelligence.Host.Composition;
using BusinessIntelligence.Modules.Jobs;
using BusinessIntelligence.Modules.Platform.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BusinessIntelligence.Host.Pages.Admin;

public sealed class JobsModel(AppDbContext database, OperatorAccess access, TimeProvider time) : PageModel
{
    public JobOverview? Overview { get; private set; }

    public TimeZoneInfo Zone { get; private set; } = TimeZoneInfo.Utc;

    /// <summary>Why the job file could not be read; the page then shows only that.</summary>
    public string? FileProblem { get; private set; }

    [TempData]
    public string? Notice { get; set; }

    public DateTimeOffset Now => time.GetUtcNow();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        if (await LoadDefinitionsAsync(cancellationToken) is { } definitions)
        {
            Zone = definitions.Zone;
            Overview = await JobOverview.LoadAsync(database, definitions, time, cancellationToken);
        }
    }

    public async Task<IActionResult> OnPostRunAsync(string job, bool force, CancellationToken cancellationToken)
    {
        if (await LoadDefinitionsAsync(cancellationToken) is not { } definitions || definitions.Jobs.All(item => item.Id != job))
        {
            Notice = "Jobul nu există în fișierul de joburi.";
            return RedirectToPage();
        }

        var (run, created) = await JobRuns.EnqueueAsync(database, job, RunTrigger.Page, force, time, cancellationToken);
        Notice = created ? null : "Jobul are deja o rulare în coadă sau în desfășurare.";
        return RedirectToPage("/Admin/Run", new { id = run.Id });
    }

    public async Task<IActionResult> OnPostCancelAsync(long run, CancellationToken cancellationToken)
    {
        Notice = await JobRuns.CancelAsync(database, run, time, cancellationToken)
            ? "Anularea a fost cerută." : "Rularea nu mai poate fi anulată.";
        return RedirectToPage("/Admin/Run", new { id = run });
    }

    public async Task<IActionResult> OnPostSignOutAsync()
    {
        await HttpContext.SignOutAsync(OperatorAccess.Scheme);
        return RedirectToPage("/Admin/SignIn");
    }

    private async Task<JobDefinitions?> LoadDefinitionsAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await access.LoadJobsAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException or IOException)
        {
            FileProblem = exception.Message;
            return null;
        }
    }
}
