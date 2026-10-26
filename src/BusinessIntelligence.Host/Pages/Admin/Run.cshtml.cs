using System.Text;
using BusinessIntelligence.Host.Composition;
using BusinessIntelligence.Modules.Jobs;
using BusinessIntelligence.Modules.Platform.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace BusinessIntelligence.Host.Pages.Admin;

public sealed class RunModel(AppDbContext database, OperatorAccess access, TimeProvider time) : PageModel
{
    /// <summary>The page shows the end of the log; the full log is a text download.</summary>
    public const int ShownLines = 1000;

    public JobRun Run { get; private set; } = null!;

    public IReadOnlyList<JobRunLine> Lines { get; private set; } = [];

    public int TotalLines { get; private set; }

    public TimeZoneInfo Zone { get; private set; } = TimeZoneInfo.Utc;

    [TempData]
    public string? Notice { get; set; }

    public DateTimeOffset Now => time.GetUtcNow();

    public bool Active => Run.Status is RunStatus.Queued or RunStatus.Running;

    public async Task<IActionResult> OnGetAsync(long id, CancellationToken cancellationToken)
    {
        if (await database.JobRuns.AsNoTracking().FirstOrDefaultAsync(item => item.Id == id, cancellationToken) is not { } run)
        {
            return NotFound();
        }

        Run = run;
        TotalLines = await database.JobRunLines.CountAsync(item => item.RunId == id, cancellationToken);
        Lines = (await database.JobRunLines.AsNoTracking().Where(item => item.RunId == id).OrderByDescending(item => item.Number)
            .Take(ShownLines).ToListAsync(cancellationToken)).AsEnumerable().Reverse().ToList();
        try
        {
            Zone = (await access.LoadJobsAsync(cancellationToken)).Zone;
        }
        catch (Exception exception) when (exception is InvalidDataException or System.Text.Json.JsonException or IOException)
        {
            // The times then show in UTC; the jobs page explains the file problem.
        }

        return Page();
    }

    public async Task<IActionResult> OnGetTextAsync(long id, CancellationToken cancellationToken)
    {
        var text = new StringBuilder();
        await foreach (var line in database.JobRunLines.AsNoTracking().Where(item => item.RunId == id).OrderBy(item => item.Number)
            .AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            text.Append(line.At.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)).Append(' ')
                .Append(line.Stream).Append(' ').AppendLine(line.Text);
        }

        return File(Encoding.UTF8.GetBytes(text.ToString()), "text/plain; charset=utf-8", $"rulare-{id}.txt");
    }
}
