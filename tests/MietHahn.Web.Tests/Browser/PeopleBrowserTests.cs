using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using MietHahn.Application.People;
using MietHahn.Domain.Communication;
using MietHahn.Domain.People;

namespace MietHahn.Web.Tests.Browser;

/// <summary>Operating the interactive person pages: dialogs, confirmations and messages.</summary>
public class PeopleBrowserTests(PostgresFixture postgres) : BrowserTestBasis(postgres)
{
    private async Task<Guid> CreatePersonAsync(PersonInput input) =>
        await App.WithScopeAsync(async sp => (await sp.GetRequiredService<IPersonService>().CreateAsync(input, null, TestContext.Current.CancellationToken)).Id!.Value);

    private async Task<IPage> OpenDetailsAsync(Guid id)
    {
        var page = await SignedInAsAdminAsync(mobile: false);
        await page.GotoAsync(BaseUrl + $"/verwaltung/personen/{id}");
        await Assertions.Expect(page.Locator("[data-interactive=true]")).ToHaveCountAsync(1);
        return page;
    }

    [Fact]
    public async Task New_person_form_shows_only_the_common_fields_until_more_details_are_opened()
    {
        var page = await SignedInAsAdminAsync(mobile: false);

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await GotoInteractiveAsync(page, "/verwaltung/personen/neu");
            var main = page.Locator("main");
            await Assertions.Expect(main.GetByLabel("Nachname bzw. Name")).ToBeVisibleAsync();
            await Assertions.Expect(main.GetByLabel("E-Mail")).ToBeVisibleAsync();
            await Assertions.Expect(main.GetByLabel("Telefon")).ToBeVisibleAsync();
            await Assertions.Expect(main.GetByLabel("Firma")).ToHaveCountAsync(0);
            await Assertions.Expect(main.GetByLabel("Geburtsdatum")).ToHaveCountAsync(0);

            await main.GetByRole(AriaRole.Button, new() { Name = "Weitere Angaben" }).ClickAsync();
            await Assertions.Expect(main.GetByLabel("Firma")).ToBeVisibleAsync();
            await Assertions.Expect(main.GetByLabel("Straße und Hausnummer")).ToBeVisibleAsync();
        });
    }

    [Fact]
    public async Task Bank_details_are_validated_added_and_removed_after_confirmation()
    {
        var id = await CreatePersonAsync(new PersonInput { FirstName = "Anna", LastName = "Schmidt" });
        var page = await OpenDetailsAsync(id);

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await OpenTabAsync(page, "Anschrift & Bank");
            await page.GetByRole(AriaRole.Button, new() { Name = "IBAN hinzufügen" }).ClickAsync();
            var dialog = page.Locator(".mud-dialog");

            await dialog.GetByLabel("IBAN").FillAsync("DE02120300000000202052"); // wrong check digits
            await dialog.GetByRole(AriaRole.Button, new() { Name = "IBAN hinzufügen" }).ClickAsync();
            await Assertions.Expect(dialog).ToContainTextAsync("Bitte eine gültige IBAN angeben");

            await dialog.GetByLabel("IBAN").FillAsync("DE02120300000000202051");
            await dialog.GetByLabel("Kontoinhaber").FillAsync("Hans Schmidt");
            await dialog.GetByRole(AriaRole.Button, new() { Name = "IBAN hinzufügen" }).ClickAsync();
            await Assertions.Expect(dialog).ToHaveCountAsync(0);
            await ExpectSnackbarAsync(page, "Die IBAN wurde hinzugefügt.");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("DE02 1203 0000 0000 2020 51");

            // Removing asks first; "Abbrechen" keeps the entry, confirming removes it.
            await page.Locator("button[aria-label^='IBAN'][aria-label$='entfernen']").ClickAsync();
            var box = page.Locator(".mud-message-box");
            await Assertions.Expect(box).ToContainTextAsync("Soll die IBAN");
            await box.GetByRole(AriaRole.Button, new() { Name = "Abbrechen" }).ClickAsync();
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("DE02 1203 0000 0000 2020 51");

            await page.Locator("button[aria-label^='IBAN'][aria-label$='entfernen']").ClickAsync();
            await box.GetByRole(AriaRole.Button, new() { Name = "Ja, entfernen" }).ClickAsync();
            await Assertions.Expect(page.Locator("main")).Not.ToContainTextAsync("DE02 1203");
        });
    }

    [Fact]
    public async Task New_address_is_entered_in_a_dialog_and_the_previous_one_stays_in_the_history()
    {
        var id = await CreatePersonAsync(new PersonInput { FirstName = "Anna", LastName = "Schmidt" });
        await App.WithScopeAsync(async sp => (await sp.GetRequiredService<IPersonService>().SetAddressAsync(
            id, new AddressInput { From = new DateOnly(2020, 1, 1), Street = "Altweg 1", PostalCode = "50667", City = "Köln" }, TestContext.Current.CancellationToken)).Succeeded);
        var page = await OpenDetailsAsync(id);

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await OpenTabAsync(page, "Anschrift & Bank");
            await page.GetByRole(AriaRole.Button, new() { Name = "Neue Anschrift" }).ClickAsync();
            var dialog = page.Locator(".mud-dialog");
            await dialog.GetByRole(AriaRole.Button, new() { Name = "Anschrift speichern" }).ClickAsync();
            await Assertions.Expect(dialog).ToContainTextAsync("Bitte Straße und Hausnummer angeben.");

            await dialog.GetByLabel("Gültig ab").FillAsync("01.03.2021");
            await dialog.GetByLabel("Straße und Hausnummer").FillAsync("Lindenweg 3");
            await dialog.GetByLabel("PLZ").FillAsync("50667");
            await dialog.GetByLabel("Ort").FillAsync("Köln");
            await dialog.GetByRole(AriaRole.Button, new() { Name = "Anschrift speichern" }).ClickAsync();

            await ExpectSnackbarAsync(page, "Die Anschrift wurde gespeichert.");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Lindenweg 3, 50667 Köln");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Frühere und künftige Anschriften");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Altweg 1");
        });
    }

    [Fact]
    public async Task Access_is_set_up_with_a_button_and_sends_the_invitation()
    {
        var id = await CreatePersonAsync(new PersonInput { LastName = "Klempner", Company = "Rohr & Co.", ContactType = ContactType.Tradesperson, Occupation = "Sanitär", Email = "rohr@example.de" });
        var page = await OpenDetailsAsync(id);

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await OpenTabAsync(page, "Zugang");
            await page.GetByRole(AriaRole.Button, new() { Name = "Zugang einrichten und einladen" }).ClickAsync();
            var box = page.GetByRole(AriaRole.Dialog);
            await Assertions.Expect(box).ToContainTextAsync("rohr@example.de");
            await box.GetByRole(AriaRole.Button, new() { Name = "Ja, einladen" }).ClickAsync();
            await ExpectSnackbarAsync(page, "Der Zugang wurde eingerichtet.");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("rohr@example.de (Handwerker)");
            await Assertions.Expect(page.Locator("#access-state")).ToContainTextAsync("Einladung wird versendet");

            await page.GetByRole(AriaRole.Button, new() { Name = "Einladung erneut senden" }).ClickAsync();
            await box.GetByRole(AriaRole.Button, new() { Name = "Ja, senden" }).ClickAsync();
            await ExpectSnackbarAsync(page, "Die Einladung wird erneut versendet.");
        });

        await App.WithDbAsync(async db =>
        {
            var mails = await db.Set<OutgoingMail>().ToListAsync(TestContext.Current.CancellationToken);
            Assert.Equal(2, mails.Count);
            Assert.All(mails, mail => Assert.Equal(["rohr@example.de"], mail.To));
        });
    }

    [Fact]
    public async Task Person_is_deleted_after_confirmation_and_the_list_confirms()
    {
        var id = await CreatePersonAsync(new PersonInput { FirstName = "Anna", LastName = "Schmidt" });
        var page = await OpenDetailsAsync(id);

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await OpenTabAsync(page, "Datenschutz");
            await page.GetByRole(AriaRole.Button, new() { Name = "Person löschen" }).ClickAsync();
            await page.Locator(".mud-message-box").GetByRole(AriaRole.Button, new() { Name = "Ja, löschen" }).ClickAsync();

            await page.WaitForURLAsync("**/verwaltung/personen?meldung=geloescht");
            await ExpectSnackbarAsync(page, "Der Eintrag wurde gelöscht.");
            await Assertions.Expect(page.Locator("tbody")).Not.ToContainTextAsync("Schmidt, Anna");
        });
    }

    [Fact]
    public async Task Anonymization_suggestion_asks_for_confirmation_and_removes_the_person_from_the_list()
    {
        var id = await CreatePersonAsync(new PersonInput { FirstName = "Anna", LastName = "Schmidt" });
        await App.WithScopeAsync(async sp => (await sp.GetRequiredService<IPersonService>().SetAddressAsync(
            id, new AddressInput { From = new DateOnly(2020, 1, 1), Street = "Altweg 1", PostalCode = "50667", City = "Köln" }, TestContext.Current.CancellationToken)).Succeeded);
        App.Clock.Advance(TimeSpan.FromDays(400)); // after the retention period
        var page = await SignedInAsAdminAsync(mobile: false);

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await page.GotoAsync(BaseUrl + "/verwaltung/datenschutz");
            await Assertions.Expect(page.Locator("[data-interactive=true]")).ToHaveCountAsync(1);
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Anna Schmidt");

            await page.Locator("button[aria-label$='anonymisieren']").ClickAsync();
            await page.Locator(".mud-message-box").GetByRole(AriaRole.Button, new() { Name = "Ja, anonymisieren" }).ClickAsync();

            await ExpectSnackbarAsync(page, "Die Person wurde anonymisiert.");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("keine Personen, deren Aufbewahrungsfrist abgelaufen ist");
        });
    }
}
