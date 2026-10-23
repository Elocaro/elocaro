using System.Net;

namespace MietHahn.Web.Tests.Account;

public class AboutPageTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    [Fact]
    public async Task About_page_is_public_and_names_version_license_and_source()
    {
        using var browser = App.Browser();

        var page = await browser.GetAsync("/ueber");

        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Contains("Über MietHahn", page.Text);
        Assert.Contains("GNU Affero General Public License", page.Text);
        Assert.NotNull(page.Document.QuerySelector("#app-version"));
        Assert.Contains(page.Document.QuerySelectorAll("a"), a => a.GetAttribute("href")!.StartsWith("https://github.com/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task User_menu_links_to_the_about_page()
    {
        var (browser, _) = await SignedInAsAsync(MietHahn.Application.Accounts.Roles.Admin);
        using (browser)
        {
            var page = await browser.GetAsync("/verwaltung");

            Assert.Contains(page.Document.QuerySelectorAll("a"), a => a.GetAttribute("href") == "/ueber");
        }
    }
}
