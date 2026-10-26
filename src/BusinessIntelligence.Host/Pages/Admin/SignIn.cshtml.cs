using System.Security.Claims;
using BusinessIntelligence.Host.Composition;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BusinessIntelligence.Host.Pages.Admin;

public sealed class SignInModel(OperatorAccess access) : PageModel
{
    [BindProperty]
    public string? Token { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public bool Refused { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!access.Accepts(Token))
        {
            // Slows guessing; the token itself is long and random.
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            Refused = true;
            return Page();
        }

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "operator")], OperatorAccess.Scheme);
        await HttpContext.SignInAsync(OperatorAccess.Scheme, new ClaimsPrincipal(identity));
        return Url.IsLocalUrl(ReturnUrl) && ReturnUrl.StartsWith(OperatorAccess.PathPrefix, StringComparison.OrdinalIgnoreCase)
            ? LocalRedirect(ReturnUrl)
            : RedirectToPage("/Admin/Jobs");
    }
}
