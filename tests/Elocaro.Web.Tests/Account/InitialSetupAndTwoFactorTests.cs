using System.Net;
using Microsoft.EntityFrameworkCore;

namespace Elocaro.Web.Tests.Account;

public class InitialSetupAndTwoFactorTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    [Fact]
    public async Task Home_page_without_sign_in_redirects_to_sign_in()
    {
        using var browser = App.Browser();

        var response = await browser.GetAsync("/", followRedirects: false);

        Assert.Equal(HttpStatusCode.Redirect, response.Status);
        Assert.StartsWith("https://localhost/konto/anmelden?ReturnUrl=%2F", response.RedirectLocation!.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/verwaltung/finanzen/offene-posten.csv")]
    [InlineData("/verwaltung/finanzen/datev/buchungen.csv?von=2026-01-01&bis=2026-12-31")]
    [InlineData("/verwaltung/tickets/routinen.pdf")]
    [InlineData("/verwaltung/einstellungen/import/vorlage.csv")]
    [InlineData("/verwaltung/gibt-es-nicht.pdf")]
    public async Task Exports_with_a_file_extension_require_two_factor_setup(string path)
    {
        using var browser = App.Browser();
        await SetUpAsync(browser);

        var response = await browser.GetAsync(path, followRedirects: false);

        Assert.Equal(HttpStatusCode.Redirect, response.Status);
        Assert.Equal("/konto/sicherheit/zwei-faktor", response.RedirectLocation!.ToString());
    }

    [Theory]
    [InlineData("/app.css")]
    [InlineData("/manifest.webmanifest")]
    [InlineData("/_framework/blazor.web.js")]
    public async Task Static_files_stay_reachable_without_two_factor(string path)
    {
        using var browser = App.Browser();
        await SetUpAsync(browser);

        var response = await browser.GetAsync(path, followRedirects: false);

        Assert.Equal(HttpStatusCode.OK, response.Status);
    }

    [Fact]
    public async Task Empty_installation_leads_to_initial_setup()
    {
        using var browser = App.Browser();

        var page = await browser.GetAsync("/konto/anmelden");

        Assert.Equal("/konto/einrichtung", page.Path);
        Assert.Contains("Ersteinrichtung", page.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Initial_setup_enforces_two_factor_before_any_other_page()
    {
        using var browser = App.Browser();

        var afterSetup = await SetUpAsync(browser);
        Assert.Equal("/konto/sicherheit/zwei-faktor", afterSetup.Path);
        Assert.Contains("vorgeschrieben", afterSetup.Text, StringComparison.Ordinal);

        // Other pages are blocked until 2FA is active.
        var start = await browser.GetAsync("/", followRedirects: false);
        Assert.Equal("/konto/sicherheit/zwei-faktor", start.RedirectLocation!.ToString());

        var (_, codes, result) = await EnableTwoFactorAsync(browser);
        Assert.Contains("Die Zwei-Faktor-Anmeldung ist jetzt aktiv.", result.Text, StringComparison.Ordinal);
        Assert.Equal(10, codes.Count);

        var afterwards = await browser.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, afterwards.Status);
        Assert.Equal("/verwaltung", afterwards.Path);
    }

    [Fact]
    public async Task Wrong_code_does_not_enable_two_factor()
    {
        using var browser = App.Browser();
        await SetUpAsync(browser);
        var page = await browser.GetAsync("/konto/sicherheit/zwei-faktor");

        var result = await browser.SubmitAsync(page, "two-factor-setup", new Dictionary<string, string> { ["Input.Code"] = "000000" });

        Assert.Contains("Der Code ist ungültig", result.Text, StringComparison.Ordinal);
        Assert.Equal("/konto/sicherheit/zwei-faktor", (await browser.GetAsync("/", followRedirects: false)).RedirectLocation!.ToString());
    }

    [Fact]
    public async Task Initial_setup_with_the_wrong_code_creates_no_account()
    {
        using var browser = App.Browser();
        var page = await browser.GetAsync("/konto/einrichtung");
        Assert.Contains("Einrichtungscode", page.Text, StringComparison.Ordinal);

        var result = await browser.SubmitAsync(page, "setup", new Dictionary<string, string>
        {
            ["Input.SetupCode"] = "falsch",
            ["Input.DisplayName"] = "Max Mustermann",
            ["Input.Email"] = AdminEmail,
            ["Input.Password"] = AdminPassword,
            ["Input.PasswordConfirmation"] = AdminPassword,
        });

        Assert.Equal("/konto/einrichtung", result.Path);
        Assert.Contains("Der Einrichtungscode stimmt nicht.", result.Text, StringComparison.Ordinal);
        await App.WithDbAsync(async db => Assert.False(await db.Users.AnyAsync(TestContext.Current.CancellationToken)));

        // The check is not the page's: without a code the form is rejected too.
        var withoutCode = await browser.SubmitAsync(result, "setup", new Dictionary<string, string>
        {
            ["Input.SetupCode"] = "",
            ["Input.DisplayName"] = "Max Mustermann",
            ["Input.Email"] = AdminEmail,
            ["Input.Password"] = AdminPassword,
            ["Input.PasswordConfirmation"] = AdminPassword,
        });
        Assert.Equal("/konto/einrichtung", withoutCode.Path);
        await App.WithDbAsync(async db => Assert.False(await db.Users.AnyAsync(TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task Initial_setup_is_only_possible_once()
    {
        using (var first = App.Browser())
        {
            await SetUpAsync(first);
        }

        using var second = App.Browser();
        var page = await second.GetAsync("/konto/einrichtung");
        Assert.Equal("/konto/anmelden", page.Path);
    }

    [Fact]
    public async Task Too_short_password_is_rejected()
    {
        using var browser = App.Browser();

        var result = await SetUpAsync(browser, AdminEmail, "kurz");

        Assert.Equal("/konto/einrichtung", result.Path);
        Assert.Contains("mindestens 12 Zeichen", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sign_in_with_password_and_two_factor_code()
    {
        string key;
        using (var setup = App.Browser())
        {
            await SetUpAsync(setup);
            (key, _, _) = await EnableTwoFactorAsync(setup);
        }

        using var browser = App.Browser();
        var twoFactor = await SignInAsync(browser, AdminEmail, AdminPassword, returnUrl: "/verwaltung/system/jobs");
        Assert.Equal("/konto/anmelden/zwei-faktor", twoFactor.Path);

        var wrong = await browser.SubmitAsync(twoFactor, "two-factor", new Dictionary<string, string> { ["Input.Code"] = "123456" });
        Assert.Contains("Der Code ist ungültig.", wrong.Text, StringComparison.Ordinal);

        var correct = await browser.SubmitAsync(wrong, "two-factor", new Dictionary<string, string> { ["Input.Code"] = Code(key) });
        Assert.Equal("/verwaltung/system/jobs", correct.Path);
        Assert.Equal(HttpStatusCode.OK, correct.Status);
    }

    [Fact]
    public async Task Recovery_code_works_exactly_once()
    {
        IReadOnlyList<string> codes;
        using (var setup = App.Browser())
        {
            await SetUpAsync(setup);
            (_, codes, _) = await EnableTwoFactorAsync(setup);
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var browser = App.Browser();
            await SignInAsync(browser, AdminEmail, AdminPassword);
            var page = await browser.GetAsync("/konto/anmelden/wiederherstellungscode");
            var result = await browser.SubmitAsync(page, "recovery", new Dictionary<string, string> { ["Input.Code"] = codes[0] });

            if (attempt == 0)
            {
                Assert.Equal("/verwaltung", result.Path);
            }
            else
            {
                Assert.Contains("Der Wiederherstellungscode ist ungültig.", result.Text, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public async Task Sign_out_ends_the_session()
    {
        using var browser = App.Browser();
        await SetUpAsync(browser);
        await EnableTwoFactorAsync(browser);

        await SignOutAsync(browser);

        var start = await browser.GetAsync("/", followRedirects: false);
        Assert.Equal(HttpStatusCode.Redirect, start.Status);
    }

    [Fact]
    public async Task Sign_out_without_antiforgery_token_is_rejected()
    {
        using var browser = App.Browser();
        await SetUpAsync(browser);
        await EnableTwoFactorAsync(browser);

        using var content = new FormUrlEncodedContent([]);
        using var response = await browser.Http.PostAsync(new Uri("/konto/abmelden", UriKind.Relative), content, TestContext.Current.CancellationToken);

        Assert.NotEqual(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/")).Status);
    }
}
