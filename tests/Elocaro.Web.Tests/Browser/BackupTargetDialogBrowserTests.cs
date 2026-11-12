using Elocaro.Application.Operations;
using Elocaro.Domain.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace Elocaro.Web.Tests.Browser;

/// <summary>Adding, changing and removing a backup target happens in a dialog, not inline on the page.</summary>
public class BackupTargetDialogBrowserTests(PostgresFixture postgres) : BrowserTestBasis(postgres)
{
    private const string Page = "/verwaltung/system/datensicherung";

    [Fact]
    public async Task A_target_is_added_changed_and_removed_in_a_dialog()
    {
        var page = await SignedInAsAdminAsync(mobile: false);

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await GotoInteractiveAsync(page, Page);
            var dialog = page.GetByRole(AriaRole.Dialog).Filter(new() { Has = page.Locator("#backup-edit") });

            // Adding opens a dialog; the page itself holds no form
            await Assertions.Expect(dialog).ToHaveCountAsync(0);
            await page.Locator("#backup-add").ClickAsync();
            await Assertions.Expect(dialog).ToBeVisibleAsync();
            await Assertions.Expect(dialog).ToContainTextAsync("Zeitplan");
            await Assertions.Expect(page.Locator("main section#backup-edit")).ToHaveCountAsync(0);

            // Cancel leaves nothing behind
            await dialog.Locator("#backup-cancel").ClickAsync();
            await Assertions.Expect(dialog).ToHaveCountAsync(0);
            await Assertions.Expect(page.Locator("#backup-targets")).ToHaveCountAsync(0);

            // Validation: without a password nothing is saved, the dialog stays open and says why
            await page.Locator("#backup-add").ClickAsync();
            await dialog.GetByLabel("Name", new() { Exact = true }).FillAsync("Keller");
            await dialog.Locator("#backup-save").ClickAsync();
            await Assertions.Expect(dialog).ToContainTextAsync("Das Passwort der Sicherung fehlt.");

            await dialog.GetByLabel("Passwort der Sicherung").FillAsync("geheimes-passwort");
            await dialog.Locator("#backup-save").ClickAsync();
            await ExpectSnackbarAsync(page, "Das Ziel wurde gespeichert.");
            await Assertions.Expect(dialog).ToHaveCountAsync(0);

            var row = page.Locator("tr[data-target='Keller']");
            await Assertions.Expect(row).ToContainTextAsync("täglich um 02:30");
            await Assertions.Expect(row).ToContainTextAsync("7 täglich, 4 wöchentlich, 12 monatlich");

            // Change: the dialog shows the saved values (the password stays hidden), the list follows
            await row.GetByRole(AriaRole.Button, new() { Name = "Bearbeiten" }).ClickAsync();
            await Assertions.Expect(dialog).ToBeVisibleAsync();
            await Assertions.Expect(dialog.GetByLabel("Name", new() { Exact = true })).ToHaveValueAsync("Keller");
            await Assertions.Expect(dialog.GetByLabel("Passwort der Sicherung")).ToHaveValueAsync("");
            await dialog.GetByLabel("Name", new() { Exact = true }).FillAsync("Keller-NAS");
            await dialog.GetByLabel("Uhrzeit").FillAsync("04:45");
            await dialog.Locator("#backup-save").ClickAsync();
            await ExpectSnackbarAsync(page, "Das Ziel wurde gespeichert.");

            var changed = page.Locator("tr[data-target='Keller-NAS']");
            await Assertions.Expect(changed).ToContainTextAsync("täglich um 04:45");
            await Assertions.Expect(page.Locator("tr[data-target='Keller']")).ToHaveCountAsync(0);

            // Remove: asks first; "No" keeps the target
            await changed.GetByRole(AriaRole.Button, new() { Name = "Bearbeiten" }).ClickAsync();
            await dialog.Locator("#backup-delete").ClickAsync();
            await page.GetByRole(AriaRole.Dialog, new() { Name = "Ziel entfernen" }).GetByRole(AriaRole.Button, new() { Name = "Abbrechen" }).ClickAsync();
            await Assertions.Expect(dialog).ToBeVisibleAsync();

            await dialog.Locator("#backup-delete").ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Ja, entfernen" }).ClickAsync();
            await ExpectSnackbarAsync(page, "Das Ziel wurde entfernt.");
            await Assertions.Expect(dialog).ToHaveCountAsync(0);
            await Assertions.Expect(page.Locator("#backup-targets")).ToHaveCountAsync(0);
        });

        Assert.Equal(0, await App.WithScopeAsync(async sp => (await sp.GetRequiredService<IBackupSettings>().ListAsync()).Count));
    }
}
