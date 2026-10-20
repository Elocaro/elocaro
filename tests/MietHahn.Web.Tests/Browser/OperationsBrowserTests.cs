using Microsoft.Playwright;

namespace MietHahn.Web.Tests.Browser;

/// <summary>Operating the mail dispatch page.</summary>
public class OperationsBrowserTests(PostgresFixture postgres) : BrowserTestBasis(postgres)
{
    [Fact]
    public async Task Test_mail_is_queued_and_shown_in_the_list()
    {
        var page = await SignedInAsAdminAsync(mobile: false);

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await GotoInteractiveAsync(page, "/verwaltung/system/mails");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Noch keine Nachrichten.");

            await page.GetByLabel("Empfänger").FillAsync("kein-at");
            await page.GetByRole(AriaRole.Button, new() { Name = "Testnachricht einreihen" }).ClickAsync();
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Bitte eine gültige E-Mail-Adresse angeben.");

            await page.GetByLabel("Empfänger").FillAsync("test@example.de");
            await page.GetByRole(AriaRole.Button, new() { Name = "Testnachricht einreihen" }).ClickAsync();
            await ExpectSnackbarAsync(page, "Testnachricht an test@example.de wurde eingereiht");
            await Assertions.Expect(page.Locator("tbody")).ToContainTextAsync("test@example.de");
        });
    }
}
