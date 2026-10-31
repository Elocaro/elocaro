using System.Net;
using System.Text.Json;
using Elocaro.Application.Accounts;
using Elocaro.Web.Tests.Account;

namespace Elocaro.Web.Tests.Operations;

public class OperationsTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    // Gotenberg is not running in these tests: the readiness check must report that.
    protected override IDictionary<string, string?> Configuration => new Dictionary<string, string?>
    {
        ["Pdf:GotenbergUrl"] = "http://127.0.0.1:1/",
        ["Hosting:HttpsRedirection"] = "false", // as in the container: TLS terminates at Caddy
    };

    [Fact]
    public async Task Liveness_check_is_reachable_without_sign_in()
    {
        using var browser = App.Browser();

        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/health/live", followRedirects: false)).Status);
    }

    [Fact]
    public async Task Readiness_reports_failed_dependencies_to_the_public_with_the_overall_status_only()
    {
        using var browser = App.Browser();

        var response = await browser.GetAsync("/health", followRedirects: false);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.Status);
        using var json = JsonDocument.Parse(response.Html);
        Assert.Equal("Unhealthy", Assert.Single(json.RootElement.EnumerateObject(), p => p.Name == "status").Value.GetString());
        Assert.Single(json.RootElement.EnumerateObject());
    }

    [Fact]
    public async Task Readiness_shows_the_checks_without_error_details_to_accounts_with_the_system_permission()
    {
        var (browser, _) = await SignedInAsAsync(Roles.Admin);
        using var _1 = browser;

        var response = await browser.GetAsync("/health", followRedirects: false);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.Status);
        using var json = JsonDocument.Parse(response.Html);
        var checks = json.RootElement.GetProperty("checks");
        Assert.Equal("Healthy", checks.GetProperty("database").GetString());
        Assert.Equal("Healthy", checks.GetProperty("file-store").GetString());
        Assert.Equal("Unhealthy", checks.GetProperty("gotenberg").GetString());
        Assert.DoesNotContain("127.0.0.1", response.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Readiness_hides_the_checks_from_accounts_without_the_system_permission()
    {
        var (browser, _) = await SignedInAsAsync(Roles.Manager);
        using var _1 = browser;

        var response = await browser.GetAsync("/health", followRedirects: false);

        using var json = JsonDocument.Parse(response.Html);
        Assert.Single(json.RootElement.EnumerateObject());
    }

    [Fact]
    public async Task Pages_with_forms_work_behind_the_proxy_with_a_secure_antiforgery_cookie()
    {
        using var browser = App.Browser();
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/konto/einrichtung");
        request.Headers.Add("X-Forwarded-Proto", "https");

        using var response = await browser.Http.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith(".AspNetCore.Antiforgery", StringComparison.Ordinal));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Responses_carry_security_headers()
    {
        using var browser = App.Browser();

        using var response = await browser.Http.GetAsync(new Uri("/konto/anmelden", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("strict-origin-when-cross-origin", response.Headers.GetValues("Referrer-Policy").Single());
        var csp = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("script-src 'self'", csp, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", csp, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, "http://")]
    [InlineData("https", "https://")]
    public async Task Behind_the_proxy_the_scheme_is_taken_from_X_Forwarded_Proto(string? proto, string expected)
    {
        using var browser = App.Browser();
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/");
        if (proto is not null)
        {
            request.Headers.Add("X-Forwarded-Proto", proto);
        }

        using var response = await browser.Http.SendAsync(request, TestContext.Current.CancellationToken);

        // The sign-in redirect uses the scheme reported by the proxy.
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith(expected, response.Headers.Location!.ToString(), StringComparison.Ordinal);
    }
}
