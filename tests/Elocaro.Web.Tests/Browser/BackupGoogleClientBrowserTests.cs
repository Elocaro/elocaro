using Elocaro.Application.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace Elocaro.Web.Tests.Browser;

/// <summary>Setting up the OAuth client for the sign-in at Google in the app itself (instead of the environment).</summary>
public class BackupGoogleClientBrowserTests(PostgresFixture postgres) : BrowserTestBasis(postgres)
{
    private const string ClientId = "123456-abc.apps.googleusercontent.com";
    private const string ClientSecret = "GOCSPX-nur-in-der-datenbank";

    [Fact]
    public async Task The_google_client_is_entered_in_the_app_and_enables_the_sign_in_without_ever_being_shown_again()
    {
        var page = await SignedInAsAdminAsync(mobile: false);

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await GotoInteractiveAsync(page, "/verwaltung/system/datensicherung");

            // Without a client the sign-in button is off and says why
            await page.Locator("#backup-add").ClickAsync();
            await SelectAsync(page, "Ablage", "Google Drive");
            await Assertions.Expect(page.Locator("#drive-not-configured")).ToContainTextAsync("noch nicht eingerichtet");
            await Assertions.Expect(page.Locator("#drive-connect-button")).ToBeDisabledAsync();
            await page.Locator("#backup-cancel").ClickAsync();

            // Set the client up on the page
            await Assertions.Expect(page.Locator("#google-none")).ToBeVisibleAsync();
            await page.Locator("#google-client").GetByLabel("Client-ID", new() { Exact = true }).FillAsync(ClientId);
            await page.Locator("#google-client").GetByLabel("Client-Schlüssel").FillAsync(ClientSecret);
            await page.Locator("#google-save").ClickAsync();
            await ExpectSnackbarAsync(page, "Die Anmeldung bei Google wurde eingerichtet.");
            await Assertions.Expect(page.Locator("#google-configured")).ToContainTextAsync(ClientId);
            await Assertions.Expect(page.Locator("#google-configured")).ToContainTextAsync("in der App");

            // Now the sign-in can start
            await page.Locator("#backup-add").ClickAsync();
            await SelectAsync(page, "Ablage", "Google Drive");
            await Assertions.Expect(page.Locator("#drive-not-configured")).ToHaveCountAsync(0);
            await Assertions.Expect(page.Locator("#drive-connect-button")).ToBeEnabledAsync();
            await page.Locator("#backup-cancel").ClickAsync();

            // Changing it shows the ID, never the secret
            await page.Locator("#google-client summary").ClickAsync();
            await Assertions.Expect(page.Locator("#google-client").GetByLabel("Client-ID", new() { Exact = true })).ToHaveValueAsync(ClientId);
            await Assertions.Expect(page.Locator("#google-client").GetByLabel("Client-Schlüssel")).ToHaveValueAsync("");
            await Assertions.Expect(page.Locator("#google-client")).ToContainTextAsync("Gespeichert. Leer lassen, um ihn zu behalten.");
        });

        Assert.DoesNotContain(ClientSecret, await page.ContentAsync(), StringComparison.Ordinal);
        var source = await App.WithScopeAsync(async sp => await sp.GetRequiredService<IGoogleClientSettings>().GetAsync(TestContext.Current.CancellationToken));
        Assert.Equal(new GoogleClientView(GoogleClientSource.App, ClientId), source);
    }
}
