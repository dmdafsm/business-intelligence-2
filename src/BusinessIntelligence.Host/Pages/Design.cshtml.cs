using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BusinessIntelligence.Host.Pages;

/// <summary>Living reference of the Material 3 components; only served in Development.</summary>
public sealed class DesignModel(IWebHostEnvironment environment) : PageModel
{
    public static readonly string[] ColorRoles =
    [
        "primary", "primary-container", "secondary", "secondary-container", "tertiary", "tertiary-container",
        "error", "error-container", "surface-container-low", "surface-container", "surface-container-high",
        "surface-container-highest", "inverse-surface", "outline",
    ];

    public static readonly string[] TypeScale =
    [
        "display-small", "headline-large", "headline-medium", "headline-small", "title-large", "title-medium",
        "title-small", "body-large", "body-medium", "body-small", "label-large", "label-medium", "label-small",
    ];

    [BindProperty(SupportsGet = true)]
    public string Tab { get; set; } = "componente";

    public IActionResult OnGet()
    {
        if (!environment.IsDevelopment())
        {
            return NotFound();
        }

        Tab = Tab is "culori" or "tipografie" ? Tab : "componente";
        return Page();
    }
}
