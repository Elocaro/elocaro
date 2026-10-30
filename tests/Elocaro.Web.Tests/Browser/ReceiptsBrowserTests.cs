using Elocaro.Application.Files;
using Elocaro.Application.Properties;
using Elocaro.Application.Receipts;
using Elocaro.Domain.Receipts;
using Elocaro.Web.Tests.Receipts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Playwright;

namespace Elocaro.Web.Tests.Browser;

/// <summary>Operating the interactive receipt pages: capturing, splitting, deleting and the automatic extraction.</summary>
public class ReceiptsBrowserTests(PostgresFixture postgres) : BrowserTestBasis(postgres)
{
    private static readonly byte[] Pdf = "%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\ntrailer << /Root 1 0 R >>\n%%EOF\n"u8.ToArray();

    private readonly StubExtractor extractor = new() { Suggestion = StubExtractor.GardenInvoice() };

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.RemoveAll<IReceiptExtractor>();
        services.AddScoped<IReceiptExtractor>(_ => extractor);
    }

    private Task<Guid> PropertyAsync() =>
        App.WithScopeAsync(async sp => (await sp.GetRequiredService<IPropertyService>().CreateAsync(
            new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, TestContext.Current.CancellationToken)).Id!.Value);

    private async Task<IPage> OpenAsync(string path)
    {
        var page = await SignedInAsAdminAsync(mobile: false);
        await GotoInteractiveAsync(page, path);
        return page;
    }

    private static FilePayload PdfFile(string name = "rechnung.pdf") => new() { Name = name, MimeType = "application/pdf", Buffer = Pdf };

    [Fact]
    public async Task Invoice_is_uploaded_and_split_into_two_cost_types_with_the_sum_checked()
    {
        await PropertyAsync();
        var page = await OpenAsync("/verwaltung/belege/neu");

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await page.Locator("input[type=file]").SetInputFilesAsync(PdfFile());
            await page.GetByLabel("oder Name").FillAsync("Grünpflege Meier");
            await page.GetByLabel("Rechnungsnummer").FillAsync("R-117");
            await page.GetByLabel("Rechnungsdatum").FillAsync("30.04.2026");
            await page.GetByLabel("Brutto").FillAsync("595");
            await page.GetByText("Weitere Angaben (Leistungszeitraum").ClickAsync();
            await page.GetByLabel("Netto").FillAsync("500");
            await page.GetByLabel("Umsatzsteuer").FillAsync("95");
            await Assertions.Expect(page.Locator("#allocation-status")).ToContainTextAsync("Noch nicht verteilt: 595,00 €");
            await page.GetByRole(AriaRole.Button, new() { Name = "Speichern" }).ClickAsync();
            await page.Locator(".mud-message-box").GetByRole(AriaRole.Button, new() { Name = "Trotzdem speichern" }).ClickAsync();

            await page.WaitForURLAsync("**/verwaltung/belege/*?meldung=gespeichert");
            await Assertions.Expect(page.Locator("h1")).ToContainTextAsync("Grünpflege Meier");
            await Assertions.Expect(page.Locator("[data-interactive=true]")).ToHaveCountAsync(1);
            await Assertions.Expect(page.Locator("#open-split")).ToContainTextAsync("Noch 595,00 € nicht aufgeteilt");
            await Assertions.Expect(page.Locator("iframe.document-preview")).ToBeVisibleAsync();

            // First position: tradesman/household service with labor costs.
            await OpenTabAsync(page, "Kostenaufteilung");
            await page.GetByRole(AriaRole.Button, new() { Name = "Position hinzufügen" }).ClickAsync();
            var dialog = page.Locator(".mud-dialog");
            await SelectAsync(page, "Kostenart", "14. Hauswart", dialog, exact: false);
            await SelectAsync(page, "Objekt", "Lindenweg 3", dialog);
            await dialog.GetByLabel("Betrag", new() { Exact = true }).FillAsync("357");
            await SelectAsync(page, "§ 35a EStG", "Haushaltsnahe Dienstleistung", dialog);
            await dialog.GetByLabel("davon Arbeitskosten").FillAsync("357");
            await dialog.GetByRole(AriaRole.Button, new() { Name = "Position hinzufügen" }).ClickAsync();
            await ExpectSnackbarAsync(page, "Die Position wurde hinzugefügt.");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Noch 238,00 € nicht aufgeteilt");

            // More than what is left is refused; without an amount the rest is taken.
            await page.GetByRole(AriaRole.Button, new() { Name = "Position hinzufügen" }).ClickAsync();
            await SelectAsync(page, "Kostenart", "10. Gartenpflege", dialog, exact: false);
            await SelectAsync(page, "Objekt", "Lindenweg 3", dialog);
            await dialog.GetByLabel("Betrag", new() { Exact = true }).FillAsync("300");
            await dialog.GetByRole(AriaRole.Button, new() { Name = "Position hinzufügen" }).ClickAsync();
            await Assertions.Expect(dialog).ToContainTextAsync("übersteigen den Rechnungsbetrag");

            await dialog.GetByLabel("Betrag", new() { Exact = true }).FillAsync("");
            await dialog.GetByRole(AriaRole.Button, new() { Name = "Position hinzufügen" }).ClickAsync();
            await Assertions.Expect(page.Locator("main")).Not.ToContainTextAsync("nicht aufgeteilt");
            await Assertions.Expect(page.Locator("tfoot")).ToContainTextAsync("595,00 €");
            await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Position hinzufügen" })).ToHaveCountAsync(0); // fully allocated
        });
    }

    [Fact]
    public async Task Receipt_with_one_cost_type_is_created_and_deleted_after_confirmation()
    {
        await PropertyAsync();
        var page = await OpenAsync("/verwaltung/belege/neu");

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await page.GetByLabel("oder Name").FillAsync("Stadt Köln");
            await page.GetByLabel("Rechnungsdatum").FillAsync("15.02.2026");
            await page.GetByLabel("Brutto").FillAsync("480");
            await page.GetByText("Weitere Angaben (Leistungszeitraum").ClickAsync();
            await page.GetByLabel("Bezahlt am").FillAsync("15.02.2026");
            await SelectAsync(page, "Kostenart", "Grundsteuer", exact: false);
            await Assertions.Expect(page.Locator("#allocation-status")).ToContainTextAsync("Vollständig verteilt: 480,00 €");
            await SelectAsync(page, "Objekt", "Lindenweg 3");
            await page.GetByRole(AriaRole.Button, new() { Name = "Speichern" }).ClickAsync();

            await page.WaitForURLAsync("**/verwaltung/belege/*?meldung=gespeichert");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Laufende öffentliche Lasten (Grundsteuer)");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("01.01.2026 – 31.12.2026");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Noch keine Datei.");
            await Assertions.Expect(page.Locator("[data-interactive=true]")).ToHaveCountAsync(1);

            await page.GetByRole(AriaRole.Button, new() { Name = "Weitere Aktionen" }).ClickAsync();
            await page.GetByRole(AriaRole.Menuitem, new() { Name = "Beleg löschen" }).ClickAsync();
            await page.Locator(".mud-message-box").GetByRole(AriaRole.Button, new() { Name = "Ja, löschen" }).ClickAsync();
            await page.WaitForURLAsync("**/verwaltung/belege?meldung=geloescht");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Keine Belege in dieser Auswahl.");
        });
    }

    [Fact]
    public async Task Invoice_is_split_over_two_positions_while_it_is_entered()
    {
        await PropertyAsync();
        var page = await OpenAsync("/verwaltung/belege/neu");

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await page.GetByLabel("oder Name").FillAsync("Hausmeisterdienst Weber");
            await page.GetByLabel("Rechnungsdatum").FillAsync("30.04.2026");
            await page.GetByLabel("Brutto").FillAsync("595");
            var first = page.Locator("fieldset.allocation-row").First;
            await SelectAsync(page, "Kostenart", "14. Hauswart", first, exact: false);
            await page.GetByRole(AriaRole.Button, new() { Name = "Auf weitere Kostenart oder weiteres Objekt aufteilen" }).ClickAsync();
            var rows = page.Locator("fieldset.allocation-row");
            await Assertions.Expect(rows).ToHaveCountAsync(2);

            // The first position keeps 595 until it is lowered: the rest is open until the second takes it.
            await rows.Nth(0).GetByLabel("Betrag", new() { Exact = true }).FillAsync("357");
            await Assertions.Expect(page.Locator("#allocation-status")).ToContainTextAsync("Noch 238,00 € nicht verteilt");
            await SelectAsync(page, "Kostenart", "10. Gartenpflege", rows.Nth(1), exact: false);
            await rows.Nth(1).GetByLabel("Betrag", new() { Exact = true }).FillAsync("238");
            await Assertions.Expect(page.Locator("#allocation-status")).ToContainTextAsync("Vollständig verteilt: 595,00 €");

            await page.GetByRole(AriaRole.Button, new() { Name = "Speichern" }).ClickAsync(); // no question: nothing is open
            await page.WaitForURLAsync("**/verwaltung/belege/*?meldung=gespeichert");
            await OpenTabAsync(page, "Kostenaufteilung");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Gartenpflege");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Hauswart");
            await Assertions.Expect(page.Locator("tfoot")).ToContainTextAsync("595,00 €");
            await Assertions.Expect(page.Locator("#open-split")).ToHaveCountAsync(0);
        });
    }

    [Fact]
    public async Task Fuel_and_supplier_statement_appear_only_for_matching_cost_types()
    {
        await PropertyAsync();
        var page = await OpenAsync("/verwaltung/belege/neu");

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Brennstoff" })).ToHaveCountAsync(0);
            await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Art der Rechnung" })).ToHaveCountAsync(0);

            await SelectAsync(page, "Kostenart", "Heizung", exact: false);
            await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Brennstoff" })).ToBeVisibleAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Art der Rechnung" })).ToBeVisibleAsync();

            await SelectAsync(page, "Kostenart", "Gartenpflege", exact: false);
            await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Brennstoff" })).ToHaveCountAsync(0);
            await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Art der Rechnung" })).ToHaveCountAsync(0);
        });
    }

    [Fact]
    public async Task Required_fields_are_reported()
    {
        var page = await OpenAsync("/verwaltung/belege/neu");

        await page.GetByLabel("Brutto").FillAsync("10");
        await page.GetByRole(AriaRole.Button, new() { Name = "Speichern" }).ClickAsync();

        await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Bitte das Rechnungsdatum angeben.");
    }

    [Fact]
    public async Task Extraction_prefills_the_form_marks_uncertain_fields_and_saves_nothing_until_confirmed()
    {
        var house = await PropertyAsync();
        _ = house;
        var page = await OpenAsync("/verwaltung/belege/neu");

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await Assertions.Expect(page.Locator("#extraction-notice")).ToContainTextAsync("an einen Dritten übermittelt");
            await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Vorschlag aus Beleg auslesen" })).ToBeDisabledAsync(); // no file yet

            await page.Locator("input[type=file]").SetInputFilesAsync(PdfFile());
            await page.GetByRole(AriaRole.Button, new() { Name = "Vorschlag aus Beleg auslesen" }).ClickAsync();

            // Pre-filled form (only one property: preselected by the user's choice later)
            await Assertions.Expect(page.GetByLabel("oder Name")).ToHaveValueAsync("Grünpflege Meier");
            await Assertions.Expect(page.GetByLabel("Rechnungsnummer")).ToHaveValueAsync("R-117");
            await Assertions.Expect(page.GetByLabel("Rechnungsdatum")).ToHaveValueAsync("30.04.2026");
            await Assertions.Expect(page.GetByLabel("Brutto")).ToHaveValueAsync(new System.Text.RegularExpressions.Regex("^595"));

            // Suggestion panel: only a suggestion, warnings, usage; uncertain fields highlighted
            await Assertions.Expect(page.Locator(".suggestion-panel")).ToContainTextAsync("nur ein Vorschlag");
            await Assertions.Expect(page.Locator("#suggestion-warnings")).ToContainTextAsync("ungewöhnlich");
            await Assertions.Expect(page.Locator("#suggestion-usage")).ToContainTextAsync("1.200 Eingabe- und 300 Ausgabe-Tokens");
            await Assertions.Expect(page.Locator(".uncertain").First).ToContainTextAsync("Unsicher erkannt");
            await Assertions.Expect(page.Locator(".uncertain", new() { HasText = "Rechnungsnummer" })).ToHaveCountAsync(1);
            await Assertions.Expect(page.Locator(".uncertain", new() { HasText = "Brutto" })).ToHaveCountAsync(0);

            // Nothing is saved yet.
            var count = await App.WithScopeAsync(async sp => (await sp.GetRequiredService<IReceiptService>().SearchAsync(new ReceiptQuery(null, null, null, false, false, null), TestContext.Current.CancellationToken)).Count);
            Assert.Equal(0, count);

            // Confirm (with a corrected invoice number): the uploaded file is attached.
            await page.GetByLabel("Rechnungsnummer").FillAsync("R-118");
            await page.GetByRole(AriaRole.Button, new() { Name = "Speichern" }).ClickAsync();
            await page.WaitForURLAsync("**/verwaltung/belege/*?meldung=gespeichert");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("R-118");
            await Assertions.Expect(page.Locator("iframe.document-preview")).ToBeVisibleAsync();
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Gartenpflege");
        });
        Assert.Single(extractor.Calls);
    }

    [Fact]
    public async Task Learned_cost_type_is_proposed_on_the_next_receipt_of_the_supplier()
    {
        await PropertyAsync();
        var page = await OpenAsync("/verwaltung/belege/neu");

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            // First receipt: the user decides for "Hauswart" instead of the proposed "Gartenpflege".
            await page.Locator("input[type=file]").SetInputFilesAsync(PdfFile("r1.pdf"));
            await page.GetByRole(AriaRole.Button, new() { Name = "Vorschlag aus Beleg auslesen" }).ClickAsync();
            await Assertions.Expect(page.GetByLabel("oder Name")).ToHaveValueAsync("Grünpflege Meier");
            await SelectAsync(page, "Kostenart", "14. Hauswart", exact: false);
            await page.GetByRole(AriaRole.Button, new() { Name = "Speichern" }).ClickAsync();
            await page.WaitForURLAsync("**/verwaltung/belege/*?meldung=gespeichert");

            // Second extraction: the model still says "Gartenpflege", but the learned rule wins.
            await GotoInteractiveAsync(page, "/verwaltung/belege/neu");
            await page.Locator("input[type=file]").SetInputFilesAsync(PdfFile("r2.pdf"));
            await page.GetByRole(AriaRole.Button, new() { Name = "Vorschlag aus Beleg auslesen" }).ClickAsync();

            await Assertions.Expect(page.Locator(".suggestion-panel")).ToContainTextAsync("aus einem früheren Beleg dieses Lieferanten gelernt");
            await Assertions.Expect(page.GetByLabel("Kostenart", new() { Exact = true }).First).ToHaveValueAsync(new System.Text.RegularExpressions.Regex("Hauswart"));
        });
    }

    [Fact]
    public async Task Section_35a_suggestion_opens_the_detail_fields()
    {
        await PropertyAsync();
        extractor.Suggestion = StubExtractor.GardenInvoice() with
        {
            Section35a = new Suggested<Section35a?>(Section35a.TradesmanService, 0.9),
            LaborCosts = new Suggested<decimal?>(357m, 0.9),
        };
        var page = await OpenAsync("/verwaltung/belege/neu");

        await page.Locator("input[type=file]").SetInputFilesAsync(PdfFile("r.pdf"));
        await page.GetByRole(AriaRole.Button, new() { Name = "Vorschlag aus Beleg auslesen" }).ClickAsync();

        await Assertions.Expect(page.GetByLabel("§ 35a EStG").First).ToHaveValueAsync("Handwerkerleistung");
        await Assertions.Expect(page.GetByLabel("davon Arbeitskosten")).ToHaveValueAsync(new System.Text.RegularExpressions.Regex("^357"));
    }

    [Fact]
    public async Task Failed_extraction_shows_the_message_and_keeps_the_manual_form()
    {
        extractor.Failure = "Die Antwort des Modells konnte nicht ausgewertet werden.";
        var page = await OpenAsync("/verwaltung/belege/neu");

        await page.Locator("input[type=file]").SetInputFilesAsync(PdfFile("r.pdf"));
        await page.GetByRole(AriaRole.Button, new() { Name = "Vorschlag aus Beleg auslesen" }).ClickAsync();

        await Assertions.Expect(page.Locator("[role=alert]").First).ToContainTextAsync("konnte nicht ausgewertet werden");
        await Assertions.Expect(page.Locator(".suggestion-panel")).ToHaveCountAsync(0);
        await Assertions.Expect(page.GetByLabel("Brutto")).ToHaveValueAsync(string.Empty); // manual entry still possible
    }

    [Fact]
    public async Task Existing_receipt_is_read_again_and_the_suggestion_is_only_applied_to_the_form()
    {
        var id = await App.WithScopeAsync(async sp =>
        {
            var ct = TestContext.Current.CancellationToken;
            await using var stream = new MemoryStream(Pdf);
            var file = await sp.GetRequiredService<IFileStore>().SaveAsync(stream, "r.pdf", ct);
            return (await sp.GetRequiredService<IReceiptService>().CreateAsync(
                new ReceiptInput { SupplierName = "Alter Name", InvoiceDate = new DateOnly(2026, 1, 1), Gross = 10m }, [file.Id], null, ct)).Id!.Value;
        });
        var page = await OpenAsync($"/verwaltung/belege/{id}");

        await page.GetByRole(AriaRole.Button, new() { Name = "Vorschlag aus Beleg auslesen" }).ClickAsync();

        await Assertions.Expect(page.GetByLabel("oder Name")).ToHaveValueAsync("Grünpflege Meier");
        await Assertions.Expect(page.GetByLabel("Rechnungsdatum")).ToHaveValueAsync("30.04.2026");
        await Assertions.Expect(page.Locator("main")).ToContainTextAsync("nur ein Vorschlag");
        Assert.Single(extractor.Calls);

        // not saved: the stored data is unchanged
        var stored = await App.WithScopeAsync(sp => sp.GetRequiredService<IReceiptService>().GetAsync(id, TestContext.Current.CancellationToken));
        Assert.Equal("Alter Name", stored!.Data.SupplierName);
        Assert.Equal(new DateOnly(2026, 1, 1), stored.Data.InvoiceDate);
    }
}
