using Elocaro.Application.Accounts;
using Elocaro.Application.Common;
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

    [Fact]
    public async Task Upload_with_a_wrong_setup_code_is_turned_away_before_the_file_is_read()
    {
        using var browser = App.Browser();

        var response = await PostUploadAsync(browser, token: await TokenAsync(browser), code: "falsch");

        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);
        Assert.EndsWith("?upload=code", response.Headers.Location!.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Upload_without_the_token_first_is_rejected()
    {
        using var browser = App.Browser();

        var response = await PostUploadAsync(browser, token: null, code: ElocaroWebFactory.SetupCode);

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_oversized_token_field_is_rejected()
    {
        using var browser = App.Browser();

        var response = await PostUploadAsync(browser, token: new string('x', 10_000), code: ElocaroWebFactory.SetupCode);

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Once_an_account_exists_the_anonymous_upload_is_closed()
    {
        using var browser = App.Browser();
        var token = await TokenAsync(browser);
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        admin.Dispose();

        var response = await PostUploadAsync(browser, token, code: ElocaroWebFactory.SetupCode);

        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);
        Assert.EndsWith("?upload=code", response.Headers.Location!.OriginalString, StringComparison.Ordinal);
    }

    private static async Task<string> TokenAsync(TestBrowser browser)
    {
        // The upload form is interactive and has no token while pre-rendering; the token is not tied to the page.
        var page = await browser.GetAsync("/konto/link");
        return page.Document.QuerySelector("input[name=__RequestVerificationToken]")!.GetAttribute("value")!;
    }

    /// <summary>Posts the upload form of the setup like a browser; <paramref name="token"/> <c>null</c> leaves the token out.</summary>
    private static async Task<HttpResponseMessage> PostUploadAsync(TestBrowser browser, string? token, string code)
    {
        using var data = new MultipartFormDataContent();
        if (token is not null)
        {
            data.Add(new StringContent(token), "__RequestVerificationToken");
        }

        data.Add(new StringContent(code), "setupCode");
        data.Add(new ByteArrayContent([1, 2, 3]), "file", "sicherung.tar");
        return await browser.Http.PostAsync("/konto/einrichtung/wiederherstellen/hochladen", data, TestContext.Current.CancellationToken);
    }
}
