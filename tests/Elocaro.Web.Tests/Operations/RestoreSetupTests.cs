using Elocaro.Application.Accounts;
using Elocaro.Web.Tests.Account;

namespace Elocaro.Web.Tests.Operations;

/// <summary>Restore from a backup in the initial setup: only for an installation without accounts.</summary>
public class RestoreSetupTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    private const string Page = "/konto/einrichtung/wiederherstellen";

    [Fact]
    public async Task Empty_installation_offers_the_restore_and_the_setup_links_to_it()
    {
        using var browser = App.Browser();

        var setup = await browser.GetAsync("/konto/einrichtung");
        var page = await browser.GetAsync(Page);

        Assert.Contains("Aus einer Datensicherung wiederherstellen", setup.Text, StringComparison.Ordinal);
        Assert.Contains("Einrichtungscode", page.Text, StringComparison.Ordinal);
        Assert.Contains("Sicherungen suchen", page.Text, StringComparison.Ordinal);
        Assert.Contains("Passwort der Sicherung", page.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Once_an_account_exists_the_restore_is_closed()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        admin.Dispose();
        using var browser = App.Browser();

        var response = await browser.GetAsync(Page, followRedirects: false);

        // Pre-rendering already sends the visitor to the sign-in
        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.Status);
        Assert.Equal("/konto/anmelden", response.RedirectLocation!.AbsolutePath);
    }

    [Fact]
    public async Task The_connection_of_interactive_pages_is_open_only_while_there_is_no_account()
    {
        using var browser = App.Browser();

        // Initial setup: the page can connect (otherwise the restore page would stay dead)
        Assert.Equal(System.Net.HttpStatusCode.OK, (await browser.GetAsync("/_blazor/initializers", followRedirects: false)).Status);

        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        admin.Dispose();

        // With an account the fallback policy applies again: sign-in required
        var closed = await browser.GetAsync("/_blazor/initializers", followRedirects: false);
        Assert.Equal(System.Net.HttpStatusCode.Redirect, closed.Status);
        Assert.Equal("/konto/anmelden", closed.RedirectLocation!.AbsolutePath);
    }
}
