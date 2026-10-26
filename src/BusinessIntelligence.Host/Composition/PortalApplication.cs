using BusinessIntelligence.Modules.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BusinessIntelligence.Host.Composition;

public static class PortalApplication
{
    public static async Task<WebApplication> BuildAsync(string[] args, string? contentRoot = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ApplicationName = typeof(PortalApplication).Assembly.FullName,
            ContentRootPath = contentRoot
        });
        var connection = await ApplicationDatabaseConnection.ResolveAsync(
            builder.Configuration.GetConnectionString("Application"), builder.Configuration["Database:PasswordFile"], CancellationToken.None)
            ?? throw new InvalidOperationException("Set ConnectionStrings__Application explicitly.");
        builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connection));
        builder.Services.AddScoped<BusinessIntelligence.Modules.Search.CompanySearch>();
        builder.Services.AddScoped<BusinessIntelligence.Modules.Profile.CompanyProfileQuery>();
        builder.Services.AddScoped<BusinessIntelligence.Modules.Market.MarketStatistics>();
        var operatorAccess = await OperatorAccess.CreateAsync(builder.Configuration);
        builder.Services.AddSingleton(operatorAccess);
        builder.Services.AddAuthentication(OperatorAccess.Scheme).AddCookie(OperatorAccess.Scheme, options =>
        {
            options.LoginPath = OperatorAccess.PathPrefix + "/intrare";
            options.Cookie.Name = "bi_operator";
            options.Cookie.Path = OperatorAccess.PathPrefix;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = true;
        });
        builder.Services.AddAuthorization();
        builder.Services.AddRazorPages(options =>
        {
            options.Conventions.AuthorizeFolder("/Admin");
            options.Conventions.AllowAnonymousToPage("/Admin/SignIn");
        });

        var app = builder.Build();
        // Never render a Development exception page; it can contain request details.
        app.UseExceptionHandler(errors => errors.Run(async context =>
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "text/plain; charset=utf-8";
            context.Response.Headers.CacheControl = "no-store";
            await context.Response.WriteAsync("Cererea nu a putut fi procesată. Reîncearcă mai târziu.", context.RequestAborted);
        }));
        if (!app.Environment.IsDevelopment())
        {
            app.UseHsts();
            app.UseHttpsRedirection();
        }

        app.Use(async (context, next) =>
        {
            context.Response.Headers.XContentTypeOptions = "nosniff";
            context.Response.Headers.ContentSecurityPolicy =
                "default-src 'self'; img-src 'self' data:; style-src 'self'; script-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
            context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            await next(context);
        });
        app.UseStatusCodePagesWithReExecute("/Eroare/{0}");
        // Operator pages exist only with a configured token, and are never cached.
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments(OperatorAccess.PathPrefix, StringComparison.OrdinalIgnoreCase))
            {
                if (!operatorAccess.Enabled)
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }

                context.Response.Headers.CacheControl = "no-store";
            }

            await next(context);
        });
        app.UseStaticFiles();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapRazorPages();
        app.MapGet("/health/live", () => Results.Ok(new { status = "alive" }));
        app.MapGet("/health/ready", async (AppDbContext database, CancellationToken cancellationToken) =>
            await DatabaseReadiness.IsReadyAsync(database, cancellationToken)
                ? Results.Ok(new { status = "ready" })
                : Results.StatusCode(StatusCodes.Status503ServiceUnavailable));
        return app;
    }
}
