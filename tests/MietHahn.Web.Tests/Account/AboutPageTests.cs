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
        Assert.Contains(page.Document.QuerySelectorAll("a"), a => a.GetAttribute("href")!.EndsWith("docs/foerderung.md", StringComparison.Ordinal));
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

    [Fact]
    public async Task Help_is_linked_in_the_user_menu_and_opens_for_landlords_and_tenants()
    {
        var (landlord, _) = await SignedInAsAsync(MietHahn.Application.Accounts.Roles.Admin);
        using (landlord)
        {
            var start = await landlord.GetAsync("/verwaltung");
            Assert.Contains(start.Document.QuerySelectorAll("a"), a => a.GetAttribute("href") == "/verwaltung/hilfe");

            var page = await landlord.GetAsync("/verwaltung/hilfe");
            Assert.Equal(HttpStatusCode.OK, page.Status);
            Assert.Contains("Sollstellung", page.Text);
            Assert.Contains("Erste Schritte", page.Text);
        }

        var (tenant, _) = await SignedInAsAsync(MietHahn.Application.Accounts.Roles.Tenant);
        using (tenant)
        {
            var page = await tenant.GetAsync("/portal/hilfe");
            Assert.Equal(HttpStatusCode.OK, page.Status);
            Assert.Contains("Kaution", page.Text);
            Assert.DoesNotContain("Sollstellung", page.Text);
        }
    }
}
