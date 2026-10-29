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
    public async Task Sign_in_page_shows_the_demo_credentials()
    {
        using var browser = App.Browser();

        var page = await browser.GetAsync("/konto/anmelden");

        Assert.Equal("/konto/anmelden", page.Path);
        Assert.Contains(DemoEmail, page.Text, StringComparison.Ordinal);
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
}
