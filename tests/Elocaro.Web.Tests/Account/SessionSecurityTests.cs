using System.Net;
using Elocaro.Application.Accounts;
using Elocaro.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Elocaro.Web.Tests.Account;

public class SessionSecurityTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    private const string Form = "sign-out-everywhere";

    [Fact]
    public async Task Signing_out_everywhere_ends_all_sessions_of_the_account_only()
    {
        var (browser, id) = await SignedInAsAsync(Roles.Admin);
        using var _ = browser;
        using var otherDevice = await SignedInAgainAsync(id);
        var (colleague, _) = await SignedInAsAsync(Roles.Admin);
        using var __ = colleague;

        var page = await browser.GetAsync("/konto/sicherheit");
        var result = await browser.SubmitAsync(page, Form);

        Assert.Equal("/konto/anmelden", result.Path);
        Assert.Contains("Sie wurden abgemeldet.", result.Text, StringComparison.Ordinal);
        Assert.Equal("/konto/anmelden", (await browser.GetAsync("/konto/sicherheit")).Path);
        Assert.Equal("/konto/anmelden", (await otherDevice.GetAsync("/konto/sicherheit")).Path);
        Assert.Equal("/konto/sicherheit", (await colleague.GetAsync("/konto/sicherheit")).Path);
    }

    [Fact]
    public async Task Signing_out_everywhere_requires_an_antiforgery_token()
    {
        var (browser, _) = await SignedInAsAsync(Roles.Tenant);
        using var _1 = browser;
        var page = await browser.GetAsync("/konto/sicherheit");
        page.Form(Form);

        using var content = new FormUrlEncodedContent([new("_handler", Form)]);
        using var response = await browser.Http.PostAsync(new Uri("/konto/sicherheit", UriKind.Relative), content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("/konto/sicherheit", (await browser.GetAsync("/konto/sicherheit")).Path);
    }

    [Fact]
    public async Task Signing_out_everywhere_also_forgets_remembered_devices()
    {
        var email = $"admin-{Guid.NewGuid():N}@example.de";
        var (first, id) = await SignedInAsAsync(Roles.Admin, email: email);
        first.Dispose();
        var (password, key) = ("Test-Passwort-2026-lang", await App.WithScopeAsync(async sp =>
        {
            var manager = sp.GetRequiredService<UserManager<ApplicationUser>>();
            return await manager.GetAuthenticatorKeyAsync((await manager.FindByIdAsync(id.ToString()))!);
        }));

        using var device = App.Browser();
        var challenge = await SignInAsync(device, email, password);
        Assert.Equal("/konto/anmelden/zwei-faktor", challenge.Path);
        await device.SubmitAsync(challenge, "two-factor", new Dictionary<string, string> { ["Input.Code"] = Code(key!), ["Input.RememberDevice"] = "true" });
        await SignOutAsync(device);

        // The device is remembered: no code needed.
        var remembered = await SignInAsync(device, email, password);
        Assert.NotEqual("/konto/anmelden/zwei-faktor", remembered.Path);

        await device.SubmitAsync(await device.GetAsync("/konto/sicherheit"), Form);

        Assert.Equal("/konto/anmelden/zwei-faktor", (await SignInAsync(device, email, password)).Path);
    }

    [Fact]
    public void All_identity_cookies_are_marked_secure()
    {
        var options = App.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>();

        foreach (var scheme in new[] { IdentityConstants.ApplicationScheme, IdentityConstants.TwoFactorUserIdScheme, IdentityConstants.TwoFactorRememberMeScheme, IdentityConstants.ExternalScheme })
        {
            Assert.Equal(CookieSecurePolicy.Always, options.Get(scheme).Cookie.SecurePolicy);
        }
    }

    [Fact]
    public async Task Antiforgery_cookie_is_sent_with_the_secure_attribute()
    {
        using var browser = App.Browser();

        using var response = await browser.Http.GetAsync(new Uri("/konto/einrichtung", UriKind.Relative), TestContext.Current.CancellationToken);

        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith(".AspNetCore.Antiforgery", StringComparison.Ordinal));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Status_cookie_is_sent_with_the_secure_attribute()
    {
        var (browser, _) = await SignedInAsAsync(Roles.Tenant);
        using var _1 = browser;
        var page = await browser.GetAsync("/konto/sicherheit/passwort");
        var form = page.Form("password-change");
        Assert.NotNull(form);

        var token = form.QuerySelector("input[name='__RequestVerificationToken']")!.GetAttribute("value")!;
        using var content = new FormUrlEncodedContent(
        [
            new("_handler", "password-change"),
            new("__RequestVerificationToken", token),
            new("Input.CurrentPassword", "Test-Passwort-2026-lang"),
            new("Input.Password", "Neues-Passwort-2026-lang"),
            new("Input.PasswordConfirmation", "Neues-Passwort-2026-lang"),
        ]);
        using var response = await browser.Http.PostAsync(new Uri("/konto/sicherheit/passwort", UriKind.Relative), content, TestContext.Current.CancellationToken);

        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("elocaro.status=", StringComparison.Ordinal));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
    }
}
