using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BusinessIntelligence.Host.Pages.Eroare;

public sealed class IndexModel : PageModel
{
    public string Title { get; private set; } = string.Empty;
    public string Message { get; private set; } = string.Empty;

    public void OnGet(int code)
    {
        (Title, Message) = code == 404
            ? ("Pagina nu există", "Adresa nu corespunde niciunei pagini. Verifică legătura sau caută din nou.")
            : ("Cererea nu a putut fi procesată", "Reîncearcă mai târziu.");
        // Re-executed pages keep the original status; a direct visit reports the code it shows.
        if (HttpContext.Features.Get<Microsoft.AspNetCore.Diagnostics.IStatusCodeReExecuteFeature>() is null)
        {
            Response.StatusCode = code is >= 400 and <= 599 ? code : 404;
        }
    }
}
