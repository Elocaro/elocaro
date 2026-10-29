using System.Net;

namespace Elocaro.Web.Tests.Account;

public class DemoModeTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    private const string DemoEmail = "demo@elocaro.de";
    private const string DemoPassword = "Demo-Passwort-2026";

    protected override IDictionary<string, string?>? Configuration => new Dictionary<string, string?>
    {
        ["Demo:Enabled"] = "true",
        ["Demo:AdminEmail"] = DemoEmail,
        ["Demo:AdminPassword"] = DemoPassword,
    };

    [Fact]
    public async Task Sign_in_page_offers_a_button_per_role_and_the_password_for_manual_sign_in()
    {
        using var browser = App.Browser();

        var page = await browser.GetAsync("/konto/anmelden");

        Assert.Equal("/konto/anmelden", page.Path);
        foreach (var role in new[] { "Administrator", "Verwalter", "Hausmeister", "Handwerker", "Eigentümer", "Mieter" })
        {
            Assert.Contains(role, page.Text, StringComparison.Ordinal);
        }

        Assert.Contains(DemoPassword, page.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Demo_user_signs_in_without_two_factor_and_sees_the_example_data()
    {
        using var browser = App.Browser();
        var page = await browser.GetAsync("/konto/anmelden");

        await browser.SubmitAsync(page, "login", new Dictionary<string, string> { ["Input.Email"] = DemoEmail, ["Input.Password"] = DemoPassword });

        var start = await browser.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, start.Status);
        Assert.Equal("/verwaltung", start.Path);
        var properties = await browser.GetAsync("/verwaltung/objekte");
        Assert.Equal("/verwaltung/objekte", properties.Path);
        Assert.Contains("Beispielhaus (Demo)", properties.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("verwalter@elocaro.de", "/verwaltung")]
    [InlineData("hausmeister@elocaro.de", "/service")]
    [InlineData("handwerker@elocaro.de", "/service")]
    [InlineData("eigentuemer@elocaro.de", "/eigentuemer")]
    [InlineData("mieter@elocaro.de", "/portal")]
    public async Task Every_role_has_a_demo_user_that_signs_in_to_its_area(string email, string area)
    {
        using var browser = App.Browser();

        await SignInAsync(browser, email, DemoPassword);

        var start = await browser.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, start.Status);
        Assert.StartsWith(area, start.Path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Passwords_cannot_be_changed_in_the_demo()
    {
        using var browser = App.Browser();
        await SignInAsync(browser, DemoEmail, DemoPassword);

        var page = await browser.GetAsync("/konto/sicherheit/passwort");

        Assert.Contains("In der Demo lassen sich Passwörter nicht ändern.", page.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Aktuelles Passwort", page.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_backup_warning_is_not_shown_in_the_demo()
    {
        using var browser = App.Browser();
        await SignInAsync(browser, DemoEmail, DemoPassword);

        var start = await browser.GetAsync("/verwaltung");

        Assert.DoesNotContain("Datensicherung prüfen", start.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("demo@elocaro.de", "/verwaltung")]
    [InlineData("mieter@elocaro.de", "/portal")]
    public async Task A_demo_role_button_signs_in_without_typing_the_password(string email, string area)
    {
        using var browser = App.Browser();
        var page = await browser.GetAsync("/konto/anmelden");

        await browser.SubmitAsync(page, "demo-login", new Dictionary<string, string> { ["DemoLogin.Email"] = email });

        var start = await browser.GetAsync("/");
        Assert.StartsWith(area, start.Path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_demo_button_does_not_sign_in_other_accounts()
    {
        using var browser = App.Browser();
        var page = await browser.GetAsync("/konto/anmelden");

        await browser.SubmitAsync(page, "demo-login", new Dictionary<string, string> { ["DemoLogin.Email"] = "unbekannt@example.de" });

        var start = await browser.GetAsync("/", followRedirects: false);
        Assert.Equal(HttpStatusCode.Redirect, start.Status);
    }
}
