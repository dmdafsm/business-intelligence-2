using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BusinessIntelligence.Host.Pages;

public sealed class IndexModel : PageModel
{
    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Query { get; set; }

    public void OnGet() => Query = string.IsNullOrWhiteSpace(Query) ? null : Query.Trim();
}
