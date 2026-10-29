using System.Net;
using Elocaro.Application.Accounts;

namespace Elocaro.Web.Tests.Account;

public class ThemeTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    [Fact]
    public async Task Theme_switch_sets_the_theme_on_all_pages_until_reset_to_system()
    {
        var (browser, _) = await SignedInAsAsync(Roles.Admin);
        using (browser)
        {
            var page = await browser.GetAsync("/verwaltung");
            Assert.Null(page.Document.DocumentElement.GetAttribute("data-theme"));

            var dark = await SwitchAsync(browser, page, "dark", "/verwaltung/personen");
            Assert.Equal("/verwaltung/personen", dark.Path);
            Assert.Equal("dark", dark.Document.DocumentElement.GetAttribute("data-theme"));
            Assert.Equal("dark", (await browser.GetAsync("/verwaltung")).Document.DocumentElement.GetAttribute("data-theme"));

            var system = await SwitchAsync(browser, await browser.GetAsync("/verwaltung"), "system", "/verwaltung");
            Assert.Null(system.Document.DocumentElement.GetAttribute("data-theme"));
        }
    }

    [Fact]
    public async Task Theme_switch_only_redirects_to_local_targets_and_ignores_unknown_values()
    {
        var (browser, _) = await SignedInAsAsync(Roles.Admin);
        using (browser)
        {
            var page = await browser.GetAsync("/verwaltung");
            using var response = await PostAsync(browser, page, "pink", "https://evil.example/");

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Equal("/", response.Headers.Location!.ToString());
            Assert.Null((await browser.GetAsync("/verwaltung")).Document.DocumentElement.GetAttribute("data-theme"));
        }
    }

    private static async Task<Page> SwitchAsync(TestBrowser browser, Page page, string theme, string returnUrl)
    {
        using var response = await PostAsync(browser, page, theme, returnUrl);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return await browser.GetAsync(response.Headers.Location!.ToString());
    }

    private static async Task<HttpResponseMessage> PostAsync(TestBrowser browser, Page page, string theme, string returnUrl)
    {
        var token = page.Document.QuerySelector("form[action='/konto/darstellung'] input[name='__RequestVerificationToken']")!.GetAttribute("value")!;
        using var content = new FormUrlEncodedContent([new("__RequestVerificationToken", token), new("theme", theme), new("returnUrl", returnUrl)]);
        return await browser.Http.PostAsync(new Uri("/konto/darstellung", UriKind.Relative), content, TestContext.Current.CancellationToken);
    }
}
