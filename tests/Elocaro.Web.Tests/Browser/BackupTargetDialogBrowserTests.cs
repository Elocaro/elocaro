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

            // Input fields are rounder than the 4 px of the baseline: the same 12 px for every kind of field
            await page.Locator("#backup-add").ClickAsync();
            await Assertions.Expect(dialog).ToBeVisibleAsync();
            var radii = await dialog.Locator(".mud-input-outlined-border").EvaluateAllAsync<string[]>("els => els.map(e => getComputedStyle(e).borderTopLeftRadius)");
            Assert.NotEmpty(radii);
            Assert.All(radii, radius => Assert.Equal("12px", radius));
            await dialog.Locator("#backup-cancel").ClickAsync();
            await Assertions.Expect(dialog).ToHaveCountAsync(0);

            // Adding opens a dialog; the page itself holds no form
            await Assertions.Expect(dialog).ToHaveCountAsync(0);
            await page.Locator("#backup-add").ClickAsync();
            await Assertions.Expect(dialog).ToBeVisibleAsync();
            await Assertions.Expect(dialog).ToContainTextAsync("Wohin soll gesichert werden?");
            await Assertions.Expect(dialog.GetByRole(AriaRole.Radio)).ToHaveCountAsync(5);

            // "Automatisch sichern" is a switch; the schedule only shows while it is on
            var automatic = dialog.GetByLabel("Automatisch sichern");
            await Assertions.Expect(dialog.Locator("#backup-schedule")).ToBeVisibleAsync();
            await automatic.SetCheckedAsync(false);
            await Assertions.Expect(dialog.Locator("#backup-schedule")).ToHaveCountAsync(0);
            await automatic.SetCheckedAsync(true);
            await Assertions.Expect(dialog.GetByLabel("Uhrzeit")).ToHaveValueAsync("02:30");
            await Assertions.Expect(page.Locator("main section#backup-edit")).ToHaveCountAsync(0);

            // Cancel leaves nothing behind
            await dialog.Locator("#backup-cancel").ClickAsync();
            await Assertions.Expect(dialog).ToHaveCountAsync(0);
            await Assertions.Expect(page.Locator("#backup-targets")).ToHaveCountAsync(0);

            // Validation: without a password saving is not possible, the dialog says what is missing
            await page.Locator("#backup-add").ClickAsync();
            await dialog.Locator("#provider-local").ClickAsync();
            // The name follows the provider: wait for that before typing over it
            await Assertions.Expect(dialog.GetByLabel("Name", new() { Exact = true })).ToHaveValueAsync("Dieser Server");
            await dialog.GetByLabel("Name", new() { Exact = true }).FillAsync("Keller");
            await Assertions.Expect(dialog.Locator("#backup-save")).ToBeDisabledAsync();
            await Assertions.Expect(dialog.Locator("#backup-test")).ToBeDisabledAsync();
            await Assertions.Expect(dialog.Locator("#backup-missing")).ToContainTextAsync("Passwort der Sicherung");

            await dialog.GetByLabel("Passwort der Sicherung").FillAsync("geheimes-passwort");
            await Assertions.Expect(dialog.Locator("#backup-save")).ToBeEnabledAsync();
            await Assertions.Expect(dialog.Locator("#backup-missing")).ToHaveCountAsync(0);
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
            await dialog.GetByLabel("Uhrzeit").PressAsync("Enter");

            // Weekly: the weekday is chosen as well
            await Assertions.Expect(dialog.GetByLabel("Wochentag")).ToHaveCountAsync(0);
            await SelectAsync(page, "Wie oft", "Wöchentlich", dialog);
            await Assertions.Expect(dialog.GetByLabel("Wochentag")).ToHaveValueAsync("Montag");
            await SelectAsync(page, "Wochentag", "Freitag", dialog);
            await dialog.Locator("#backup-save").ClickAsync();
            await ExpectSnackbarAsync(page, "Das Ziel wurde gespeichert.");

            var changed = page.Locator("tr[data-target='Keller-NAS']");
            await Assertions.Expect(changed).ToContainTextAsync("wöchentlich freitags um 04:45");
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
