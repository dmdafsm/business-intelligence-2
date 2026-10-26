using System.Net;
using Xunit;

namespace BusinessIntelligence.BrowserTests;

/// <summary>The operator portal behind a TLS-terminating proxy that sends X-Forwarded-Proto.</summary>
public sealed class ProxiedAdminPortalFixture : AdminPortalFixture
{
    protected override IEnumerable<string> PortalArguments => [.. base.PortalArguments, "--Proxy:TrustForwardedProto", "true"];
}

public sealed class ForwardedProtoTests(ProxiedAdminPortalFixture portal) : IClassFixture<ProxiedAdminPortalFixture>
{
    [Fact]
    public async Task TheSignInRedirectAndOperatorCookieFollowTheForwardedScheme()
    {
        var cookies = new CookieContainer();
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = cookies });

        using var protectedPage = new HttpRequestMessage(HttpMethod.Get, portal.Address + "/admin/joburi");
        protectedPage.Headers.Add("X-Forwarded-Proto", "https");
        using var redirect = await client.SendAsync(protectedPage);
        Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
        Assert.Equal("https", redirect.Headers.Location!.Scheme);

        // The sign-in form carries an antiforgery token; posting it back sets the operator cookie.
        using var form = new HttpRequestMessage(HttpMethod.Get, portal.Address + "/admin/intrare");
        form.Headers.Add("X-Forwarded-Proto", "https");
        var html = await (await client.SendAsync(form)).Content.ReadAsStringAsync();
        var antiforgery = System.Text.RegularExpressions.Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(antiforgery);

        using var signIn = new HttpRequestMessage(HttpMethod.Post, portal.Address + "/admin/intrare")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Token"] = AdminPortalFixture.Token,
                ["__RequestVerificationToken"] = antiforgery,
            }),
        };
        signIn.Headers.Add("X-Forwarded-Proto", "https");
        using var signedIn = await client.SendAsync(signIn);
        var operatorCookie = signedIn.Headers.GetValues("Set-Cookie").Single(header => header.StartsWith("bi_operator=", StringComparison.Ordinal));
        Assert.Contains("; secure", operatorCookie, StringComparison.OrdinalIgnoreCase);
    }
}
