using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using MietHahn.Application.Properties;
using MietHahn.Domain.Properties;
using SkiaSharp;

namespace MietHahn.Web.Tests.Browser;

/// <summary>Operating the interactive property, unit and room pages.</summary>
public class PropertiesBrowserTests(PostgresFixture postgres) : BrowserTestBasis(postgres)
{
    private async Task<Guid> CreateHouseAsync() =>
        await App.WithScopeAsync(async sp => (await sp.GetRequiredService<IPropertyService>().CreateAsync(
            new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, TestContext.Current.CancellationToken)).Id!.Value);

    private async Task<IPage> OpenAsync(string path)
    {
        var page = await SignedInAsAdminAsync(mobile: false);
        await page.GotoAsync(BaseUrl + path);
        await Assertions.Expect(page.Locator("[data-interactive=true]")).ToHaveCountAsync(1);
        return page;
    }

    [Fact]
    public async Task Property_is_created_with_validation_and_the_details_page_confirms()
    {
        var page = await OpenAsync("/verwaltung/objekte/neu");

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await page.GetByLabel("Bezeichnung").FillAsync("Lindenweg 3");
            await page.GetByLabel("Straße und Hausnummer").FillAsync("Lindenweg 3");
            await page.GetByLabel("PLZ").FillAsync("123");
            await page.GetByLabel("Ort").FillAsync("Köln");
            await page.GetByLabel("Baujahr").FillAsync("1200");
            await page.GetByRole(AriaRole.Button, new() { Name = "Objekt anlegen", Exact = true }).ClickAsync();
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Die Postleitzahl hat 5 Ziffern.");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Baujahr zwischen 1500 und 2100");

            await page.GetByLabel("PLZ").FillAsync("50667");
            await page.GetByLabel("Baujahr").FillAsync("1965");
            await page.GetByRole(AriaRole.Button, new() { Name = "Objekt anlegen", Exact = true }).ClickAsync();

            await page.WaitForURLAsync("**/verwaltung/objekte/*?meldung=objekt-angelegt");
            await ExpectSnackbarAsync(page, "Das Objekt wurde angelegt.");
            await Assertions.Expect(page.Locator(".record-header")).ToContainTextAsync("Lindenweg 3, 50667 Köln");
        });
    }

    [Fact]
    public async Task Owners_are_entered_with_shares_and_a_total_above_100_percent_is_refused()
    {
        var propertyId = await CreateHouseAsync();
        await App.WithScopeAsync(async sp =>
        {
            var people = sp.GetRequiredService<MietHahn.Application.People.IPersonService>();
            await people.CreateAsync(new MietHahn.Application.People.PersonInput { FirstName = "Anna", LastName = "Schmidt" }, null, TestContext.Current.CancellationToken);
            await people.CreateAsync(new MietHahn.Application.People.PersonInput { FirstName = "Ben", LastName = "Weber" }, null, TestContext.Current.CancellationToken);
            return true;
        });
        var page = await OpenAsync($"/verwaltung/objekte/{propertyId}");

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await OpenTabAsync(page, "Eigentümer");
            await page.GetByRole(AriaRole.Button, new() { Name = "Eigentümer hinzufügen" }).ClickAsync();
            var dialog = page.Locator(".mud-dialog");
            await SelectAsync(page, "Person", "Anna Schmidt", dialog, exact: false);
            await dialog.GetByLabel("Anteil").FillAsync("60");
            await dialog.GetByRole(AriaRole.Button, new() { Name = "Eigentümer hinzufügen" }).ClickAsync();
            await ExpectSnackbarAsync(page, "Der Eigentümer wurde eingetragen.");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("60 %");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("ergeben zusammen 60 % statt 100 %");

            await page.GetByRole(AriaRole.Button, new() { Name = "Eigentümer hinzufügen" }).ClickAsync();
            await SelectAsync(page, "Person", "Ben Weber", dialog, exact: false);
            await dialog.GetByLabel("Anteil").FillAsync("50");
            await dialog.GetByRole(AriaRole.Button, new() { Name = "Eigentümer hinzufügen" }).ClickAsync();
            await Assertions.Expect(dialog).ToContainTextAsync("mehr als 100 %");
        });
    }

    [Fact]
    public async Task Units_rooms_and_areas_are_entered_in_dialogs()
    {
        var propertyId = await CreateHouseAsync();
        var page = await OpenAsync($"/verwaltung/objekte/{propertyId}");

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            // Area with German decimal comma; the retroactive date is rejected with the domain message.
            await OpenTabAsync(page, "Flächen");
            await page.GetByRole(AriaRole.Button, new() { Name = "Fläche erfassen" }).ClickAsync();
            var dialog = page.Locator(".mud-dialog");
            await dialog.GetByLabel("Gültig ab").FillAsync("01.02.2026");
            await dialog.GetByLabel("Gesamtfläche").FillAsync("400");
            await dialog.GetByLabel("Gemeinschaftsfläche").FillAsync("30,5");
            await dialog.GetByRole(AriaRole.Button, new() { Name = "Fläche speichern" }).ClickAsync();
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("30,5 m²");

            await page.GetByRole(AriaRole.Button, new() { Name = "Fläche ändern" }).ClickAsync();
            await dialog.GetByLabel("Gültig ab").FillAsync("01.01.2026");
            await dialog.GetByRole(AriaRole.Button, new() { Name = "Fläche speichern" }).ClickAsync();
            await Assertions.Expect(dialog).ToContainTextAsync("Der Stichtag muss am oder nach dem 01.02.2026 liegen");
            await dialog.GetByRole(AriaRole.Button, new() { Name = "Abbrechen" }).ClickAsync();

            // Add a unit (name required), then open it and add a room.
            await OpenTabAsync(page, "Einheiten");
            await page.GetByRole(AriaRole.Button, new() { Name = "Einheit hinzufügen" }).ClickAsync();
            await dialog.GetByRole(AriaRole.Button, new() { Name = "Einheit hinzufügen", Exact = true }).ClickAsync();
            await Assertions.Expect(dialog).ToContainTextAsync("Bezeichnung");
            await dialog.GetByLabel("Bezeichnung").FillAsync("Wohnung 4");
            await dialog.GetByLabel("Lage").FillAsync("OG");
            await dialog.GetByLabel("Wohnfläche").FillAsync("55,5");
            await dialog.GetByRole(AriaRole.Button, new() { Name = "Speichern und weitere anlegen" }).ClickAsync();
            await ExpectSnackbarAsync(page, "Die Einheit wurde hinzugefügt.");
            await Assertions.Expect(dialog.GetByLabel("Bezeichnung")).ToHaveValueAsync("");
            await dialog.GetByLabel("Bezeichnung").FillAsync("Wohnung 5");
            await dialog.GetByLabel("Wohnfläche").FillAsync("40");
            await dialog.GetByRole(AriaRole.Button, new() { Name = "Einheit hinzufügen", Exact = true }).ClickAsync();
            await ExpectSnackbarAsync(page, "Die Einheit wurde hinzugefügt.");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("55,5");
            await Assertions.Expect(page.Locator("main")).Not.ToContainTextAsync("Keine Wohnfläche");
            await page.GetByRole(AriaRole.Link, new() { Name = "Wohnung 4" }).ClickAsync();

            await page.WaitForURLAsync("**/verwaltung/einheiten/*");
            await Assertions.Expect(page.Locator("[data-interactive=true]")).ToHaveCountAsync(1);
            await page.GetByRole(AriaRole.Button, new() { Name = "Zimmer hinzufügen" }).ClickAsync();
            await dialog.GetByLabel("Bezeichnung").FillAsync("Zimmer 1");
            await dialog.GetByRole(AriaRole.Button, new() { Name = "Zimmer hinzufügen" }).ClickAsync();
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Zimmer 1");
        });
    }

    [Fact]
    public async Task Photo_is_uploaded_rejected_when_not_an_image_and_removed_after_confirmation()
    {
        var propertyId = await CreateHouseAsync();
        var page = await OpenAsync($"/verwaltung/objekte/{propertyId}");

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await OpenTabAsync(page, "Foto");
            var input = page.Locator("input[type=file]");
            await input.SetInputFilesAsync(new FilePayload { Name = "plan.pdf", MimeType = "application/pdf", Buffer = "%PDF-1.4"u8.ToArray() });
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Das Foto muss ein Bild sein.");

            await input.SetInputFilesAsync(new FilePayload { Name = "house.jpg", MimeType = "image/jpeg", Buffer = Jpeg() });
            await ExpectSnackbarAsync(page, "Das Foto wurde gespeichert.");
            await Assertions.Expect(page.Locator("img.property-image")).ToBeVisibleAsync();
            var source = await page.Locator("img.property-image").GetAttributeAsync("src");
            await using var response = await page.APIRequest.GetAsync(BaseUrl + source);
            Assert.Equal("image/jpeg", response.Headers["content-type"]);

            await page.GetByRole(AriaRole.Button, new() { Name = "Foto entfernen" }).ClickAsync();
            await page.Locator(".mud-message-box").GetByRole(AriaRole.Button, new() { Name = "Ja, Foto entfernen" }).ClickAsync();
            await Assertions.Expect(page.Locator("img.property-image")).ToHaveCountAsync(0);
        });
    }

    [Fact]
    public async Task Property_without_units_is_deleted_after_confirmation()
    {
        var propertyId = await CreateHouseAsync();
        var page = await OpenAsync($"/verwaltung/objekte/{propertyId}");

        await page.GetByRole(AriaRole.Button, new() { Name = "Weitere Aktionen" }).ClickAsync();
        await page.GetByRole(AriaRole.Menuitem, new() { Name = "Objekt löschen" }).ClickAsync();
        await page.Locator(".mud-message-box").GetByRole(AriaRole.Button, new() { Name = "Ja, löschen" }).ClickAsync();

        await page.WaitForURLAsync("**/verwaltung/objekte?meldung=geloescht");
        await ExpectSnackbarAsync(page, "Der Eintrag wurde gelöscht.");
        await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Noch keine Objekte");
    }

    private static byte[] Jpeg()
    {
        using var bitmap = new SKBitmap(40, 30);
        bitmap.Erase(SKColors.CornflowerBlue);
        using var image = SKImage.FromBitmap(bitmap);
        return image.Encode(SKEncodedImageFormat.Jpeg, 80).ToArray();
    }
}
