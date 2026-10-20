using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using MietHahn.Application.Properties;

namespace MietHahn.Web.Tests.Browser;

/// <summary>Operating the interactive document pages: upload, validation, editing, deleting.</summary>
public class DocumentsBrowserTests(PostgresFixture postgres) : BrowserTestBasis(postgres)
{
    private static readonly byte[] Pdf = "%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\ntrailer << /Root 1 0 R >>\n%%EOF\n"u8.ToArray();

    private async Task<Guid> CreateHouseAsync() =>
        await App.WithScopeAsync(async sp => (await sp.GetRequiredService<IPropertyService>().CreateAsync(
            new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, TestContext.Current.CancellationToken)).Id!.Value);

    [Fact]
    public async Task Document_is_uploaded_with_the_file_name_as_title_edited_shared_and_deleted()
    {
        var propertyId = await CreateHouseAsync();
        var page = await SignedInAsAdminAsync(mobile: false);

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            // Link from the property: the reference is preselected.
            await GotoInteractiveAsync(page, $"/verwaltung/dokumente/neu?objekt={propertyId}");
            await Assertions.Expect(page.GetByLabel("Objekt", new() { Exact = true }).First).ToHaveValueAsync("Lindenweg 3");

            await page.Locator("input[type=file]").SetInputFilesAsync(new FilePayload { Name = "Energieausweis_2024.pdf", MimeType = "application/pdf", Buffer = Pdf });
            await SelectAsync(page, "Kategorie", "Energieausweis");
            await page.GetByLabel("Gültig bis").FillAsync("31.05.2034");
            await page.GetByLabel("Schlagworte").FillAsync("Energie, Pflicht");
            await page.GetByRole(AriaRole.Button, new() { Name = "Hochladen" }).ClickAsync();

            await page.WaitForURLAsync("**/verwaltung/dokumente/*?meldung=angelegt");
            await ExpectSnackbarAsync(page, "Der Eintrag wurde angelegt.");
            await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync("Energieausweis_2024");
            await Assertions.Expect(page.Locator("iframe.document-preview")).ToBeVisibleAsync();

            await Assertions.Expect(page.Locator("[data-interactive=true]")).ToHaveCountAsync(1);
            await page.GetByLabel("Titel").FillAsync("Energieausweis (Bedarf)");
            await page.GetByLabel("Für Hausmeister freigeben").CheckAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Speichern" }).ClickAsync();
            await ExpectSnackbarAsync(page, "Die Angaben wurden gespeichert.");
            await Assertions.Expect(page.Locator("dl.details")).ToContainTextAsync("Hausmeister");

            await page.GetByRole(AriaRole.Button, new() { Name = "Dokument löschen" }).ClickAsync();
            await page.Locator(".mud-message-box").GetByRole(AriaRole.Button, new() { Name = "Ja, löschen" }).ClickAsync();
            await page.WaitForURLAsync("**/verwaltung/dokumente?meldung=geloescht");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Noch keine Dokumente.");
        });
    }

    [Fact]
    public async Task Upload_without_reference_or_with_an_executable_stores_nothing()
    {
        await CreateHouseAsync();
        var page = await SignedInAsAdminAsync(mobile: false);

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await GotoInteractiveAsync(page, "/verwaltung/dokumente/neu");
            await page.GetByRole(AriaRole.Button, new() { Name = "Hochladen" }).ClickAsync();
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Bitte eine Datei auswählen.");

            await page.Locator("input[type=file]").SetInputFilesAsync(new FilePayload { Name = "lose.pdf", MimeType = "application/pdf", Buffer = Pdf });
            await page.GetByRole(AriaRole.Button, new() { Name = "Hochladen" }).ClickAsync();
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("mindestens einen Bezug");

            await SelectAsync(page, "Objekt", "Lindenweg 3");
            await page.Locator("input[type=file]").SetInputFilesAsync(new FilePayload { Name = "tool.exe", MimeType = "application/octet-stream", Buffer = "MZ"u8.ToArray() });
            await page.GetByRole(AriaRole.Button, new() { Name = "Hochladen" }).ClickAsync();
            await Assertions.Expect(page.Locator(".message-error")).ToBeVisibleAsync();

            await GotoInteractiveAsync(page, "/verwaltung/dokumente");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Noch keine Dokumente.");
        });
    }

    [Fact]
    public async Task Document_list_filters_live_by_text_and_category()
    {
        var propertyId = await CreateHouseAsync();
        await App.WithScopeAsync(async sp =>
        {
            var ct = TestContext.Current.CancellationToken;
            foreach (var (name, category) in new[] { ("Heizungswartung", MietHahn.Domain.Documents.DocumentCategory.Miscellaneous), ("Rechnung Strom", MietHahn.Domain.Documents.DocumentCategory.Invoice) })
            {
                await using var stream = new MemoryStream(Pdf);
                var file = await sp.GetRequiredService<MietHahn.Application.Files.IFileStore>().SaveAsync(stream, name + ".pdf", ct);
                await sp.GetRequiredService<MietHahn.Application.Documents.IDocumentLibrary>().CreateAsync(file.Id, new MietHahn.Application.Documents.DocumentInput { Title = name, Category = category, PropertyId = propertyId }, ct);
            }

            return 0;
        });
        var page = await SignedInAsAdminAsync(mobile: false);

        await GotoInteractiveAsync(page, "/verwaltung/dokumente");
        await Assertions.Expect(page.Locator("tbody tr")).ToHaveCountAsync(2);
        await SelectAsync(page, "Kategorie", "Rechnung");
        await Assertions.Expect(page.Locator("tbody tr")).ToHaveCountAsync(1);
        await Assertions.Expect(page.Locator("tbody")).ToContainTextAsync("Rechnung Strom");
    }
}
