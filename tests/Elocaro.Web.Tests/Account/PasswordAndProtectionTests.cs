using System.Net;
using Elocaro.Infrastructure.Identity;
using Elocaro.Web.Account;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Elocaro.Web.Tests.Account;

public class PasswordAndProtectionTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    [Fact]
    public async Task Wrong_password_gives_a_neutral_message()
    {
        using (var setup = App.Browser())
        {
            await SetUpAsync(setup);
        }

        using var browser = App.Browser();
        var wrong = await SignInAsync(browser, AdminEmail, "Falsches-Passwort-123");
        var unknown = await SignInAsync(browser, "niemand@example.de", "Falsches-Passwort-123");

        Assert.Contains("E-Mail-Adresse oder Passwort ist falsch.", wrong.Text, StringComparison.Ordinal);
        Assert.Contains("E-Mail-Adresse oder Passwort ist falsch.", unknown.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task After_five_failed_attempts_the_account_is_temporarily_locked()
    {
        using (var setup = App.Browser())
        {
            await SetUpAsync(setup);
        }

        using var browser = App.Browser();
        Page page = null!;
        for (var i = 0; i < 5; i++)
        {
            page = await SignInAsync(browser, AdminEmail, "Falsches-Passwort-123");
        }

        Assert.Equal("/konto/gesperrt", page.Path);
        Assert.Equal("/konto/gesperrt", (await SignInAsync(browser, AdminEmail, AdminPassword)).Path);

        // Identity uses the system clock: check the lockout duration and simulate its expiry.
        await App.WithScopeAsync(async sp =>
        {
            var manager = sp.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await manager.FindByEmailAsync(AdminEmail))!;
            var end = await manager.GetLockoutEndDateAsync(user);
            Assert.InRange(end!.Value - DateTimeOffset.UtcNow, TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(15));
            return await manager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddSeconds(-1));
        });

        Assert.Equal("/konto/sicherheit/zwei-faktor", (await SignInAsync(browser, AdminEmail, AdminPassword)).Path);
    }

    [Fact]
    public async Task Forgot_and_reset_password()
    {
        using (var setup = App.Browser())
        {
            await SetUpAsync(setup);
        }

        using var browser = App.Browser();
        var request = await browser.GetAsync("/konto/passwort-vergessen");
        var confirmation = await browser.SubmitAsync(request, "password-forgot", new Dictionary<string, string> { ["Input.Email"] = AdminEmail });
        Assert.Contains("Falls ein passendes Konto existiert", confirmation.Text, StringComparison.Ordinal);

        var mail = (await LastMailToAsync(AdminEmail))!;
        Assert.Equal("Passwort zurücksetzen", mail.Subject);
        var form = await browser.GetAsync(ExtractLink(mail));
        var result = await browser.SubmitAsync(form, "password-reset", new Dictionary<string, string>
        {
            ["Input.Password"] = "Neues-Passwort-2026!",
            ["Input.PasswordConfirmation"] = "Neues-Passwort-2026!",
        });

        Assert.Equal("/konto/anmelden", result.Path);
        Assert.Contains("Ihr Passwort wurde geändert", result.Text, StringComparison.Ordinal);
        Assert.Contains("E-Mail-Adresse oder Passwort ist falsch.", (await SignInAsync(browser, AdminEmail, AdminPassword)).Text, StringComparison.Ordinal);
        // An admin without 2FA set up lands directly on the mandatory setup after signing in.
        Assert.Equal("/konto/sicherheit/zwei-faktor", (await SignInAsync(browser, AdminEmail, "Neues-Passwort-2026!")).Path);
    }

    [Fact]
    public async Task Reset_link_can_only_be_used_once()
    {
        using var browser = App.Browser();
        await SetUpAsync(browser);
        await browser.SubmitAsync(await browser.GetAsync("/konto/passwort-vergessen"), "password-forgot", new Dictionary<string, string> { ["Input.Email"] = AdminEmail });
        var link = ExtractLink((await LastMailToAsync(AdminEmail))!);
        var fields = new Dictionary<string, string>
        {
            ["Input.Password"] = "Neues-Passwort-2026!",
            ["Input.PasswordConfirmation"] = "Neues-Passwort-2026!",
        };

        await browser.SubmitAsync(await browser.GetAsync(link), "password-reset", fields);
        var secondTime = await browser.SubmitAsync(await browser.GetAsync(link), "password-reset", fields);

        Assert.Contains("Der Link ist ungültig oder abgelaufen.", secondTime.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Token_lifetimes_are_2_hours_for_reset_and_7_days_for_invitations()
    {
        // Identity checks the lifetime itself (using the system clock); here the configuration is verified.
        Assert.Equal(TimeSpan.FromHours(2), App.Services.GetRequiredService<IOptions<DataProtectionTokenProviderOptions>>().Value.TokenLifespan);
        Assert.Equal(TimeSpan.FromDays(7), App.Services.GetRequiredService<IOptions<InvitationTokenOptions>>().Value.TokenLifespan);
    }

    [Fact]
    public async Task Locked_account_cannot_sign_in()
    {
        using (var setup = App.Browser())
        {
            await SetUpAsync(setup);
        }

        await App.WithScopeAsync(async sp =>
        {
            var manager = sp.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await manager.FindByEmailAsync(AdminEmail);
            user!.Locked = true;
            return await manager.UpdateAsync(user);
        });

        using var browser = App.Browser();
        Assert.Contains("E-Mail-Adresse oder Passwort ist falsch.", (await SignInAsync(browser, AdminEmail, AdminPassword)).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task External_return_url_is_ignored()
    {
        using var browser = App.Browser();
        await SetUpAsync(browser);
        var (key, _, _) = await EnableTwoFactorAsync(browser);
        await SignOutAsync(browser);

        var twoFactor = await SignInAsync(browser, AdminEmail, AdminPassword, returnUrl: "https://boese.example/phishing");
        var result = await browser.SubmitAsync(twoFactor, "two-factor", new Dictionary<string, string> { ["Input.Code"] = Code(key) }, followRedirects: false);

        Assert.Equal("/", result.RedirectLocation?.ToString().Replace("https://localhost", "", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("/verwaltung", "/verwaltung")]
    [InlineData("verwaltung?x=1", "/verwaltung?x=1")]
    [InlineData("https://boese.example", "/")]
    [InlineData("//boese.example", "/")]
    [InlineData("/\\boese.example", "/")]
    public void Only_local_redirect_targets_are_allowed(string? destination, string expected)
    {
        Assert.Equal(expected, Redirector.LocalTarget(destination));
    }
}

public class RateLimitTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    protected override IDictionary<string, string?> Configuration => new Dictionary<string, string?> { ["SignIn:RequestsPerMinute"] = "3" };

    [Fact]
    public async Task Too_many_sign_in_attempts_are_throttled()
    {
        using var browser = App.Browser();
        var page = await browser.GetAsync("/konto/link");

        var status = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            status.Add((await browser.SubmitAsync(page, "magic-link", new Dictionary<string, string> { ["Input.Email"] = "x@example.de" })).Status);
        }

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], status);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/konto/link")).Status); // GET remains allowed
    }
}
