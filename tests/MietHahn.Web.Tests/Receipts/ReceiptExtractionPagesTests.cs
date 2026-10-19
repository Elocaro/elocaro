using System.Net;
using AngleSharp.Html.Dom;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MietHahn.Application.Accounts;
using MietHahn.Application.Properties;
using MietHahn.Application.Receipts;
using MietHahn.Domain.Receipts;
using MietHahn.Infrastructure.Receipts.Extraction;
using MietHahn.Web.Tests.Account;

namespace MietHahn.Web.Tests.Receipts;

/// <summary>Receipt form with "Vorschlag aus Beleg auslesen": the result pre-fills the form, the user confirms.</summary>
public class ReceiptExtractionPagesTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    private static readonly byte[] Pdf = "%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\ntrailer << /Root 1 0 R >>\n%%EOF\n"u8.ToArray();

    private readonly StubExtractor extractor = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.RemoveAll<IReceiptExtractor>();
        services.AddScoped<IReceiptExtractor>(_ => extractor);
    }

    [Fact]
    public async Task Without_provider_there_is_no_extraction_button_and_the_settings_say_so()
    {
        extractor.Enabled = false;
        extractor.External = false;
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;

        var form = await admin.GetAsync("/verwaltung/belege/neu");
        var settings = await admin.GetAsync("/verwaltung/system/belegauslesung");

        Assert.DoesNotContain("Vorschlag aus Beleg auslesen", form.Text, StringComparison.Ordinal);
        Assert.Null(form.Document.QuerySelector("#extraction-notice"));
        Assert.Contains("Keiner", settings.Document.QuerySelector("#provider-name")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("ausgeschaltet", settings.Text, StringComparison.Ordinal);
        Assert.Null(settings.Document.QuerySelector("#third-party-notice"));
    }

    [Fact]
    public async Task Provider_not_ready_shows_the_notice_and_the_problem_but_no_upload()
    {
        extractor.Ready = false;
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;

        var form = await admin.GetAsync("/verwaltung/belege/neu");

        Assert.Contains("an einen Dritten übermittelt", form.Document.QuerySelector("#extraction-notice")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("kein API-Schlüssel", form.Document.QuerySelector("#extraction-problem")!.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Vorschlag aus Beleg auslesen", form.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Extraction_prefills_the_form_marks_uncertain_fields_and_saves_nothing_until_confirmed()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var house = await PropertyAsync();
        extractor.Suggestion = Suggestion();

        var page = await admin.GetAsync("/verwaltung/belege/neu");
        Assert.Contains("an einen Dritten übermittelt", page.Document.QuerySelector("#extraction-notice")!.TextContent, StringComparison.Ordinal);
        page = await admin.SubmitWithFileAsync(page, "extract", "ExtractFiles", Pdf, "rechnung.pdf", "application/pdf");

        // Pre-filled form
        Assert.Equal("Grünpflege Meier", Value(page, "#receipt-supplier-name"));
        Assert.Equal("R-117", Value(page, "#receipt-number"));
        Assert.Equal("2026-04-30", Value(page, "#receipt-date"));
        Assert.Equal("595", Value(page, "#receipt-gross").Replace(".00", "", StringComparison.Ordinal));
        Assert.Equal(house.ToString(), Selected(page, "#item-property")); // only one property
        Assert.Equal("betrkv-10", Selected(page, "#item-cost-type"));

        // Suggestion panel: only a suggestion, warnings, usage; uncertain fields highlighted
        Assert.Contains("nur ein Vorschlag", page.Document.QuerySelector(".suggestion-panel")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("ungewöhnlich", page.Document.QuerySelector("#suggestion-warnings")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("1.200 Eingabe- und 300 Ausgabe-Tokens", page.Document.QuerySelector("#suggestion-usage")!.TextContent, StringComparison.Ordinal);
        var uncertain = page.Document.QuerySelectorAll(".field-uncertain").Select(f => f.QuerySelector("label")!.TextContent.Trim()).ToList();
        Assert.Contains("Kostenart", uncertain);
        Assert.Contains("Rechnungsnummer", uncertain);
        Assert.DoesNotContain("Brutto *", uncertain);
        Assert.Contains("Unsicher erkannt", page.Document.QuerySelector(".field-uncertain")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("1 Datei ist bereits hochgeladen", page.Document.QuerySelector("#stored-files")!.TextContent, StringComparison.Ordinal);

        // Nothing saved yet
        Assert.Contains("Keine Belege", (await admin.GetAsync("/verwaltung/belege")).Text, StringComparison.Ordinal);

        // Confirm (with a corrected invoice number): the uploaded file is attached without uploading it again
        var saved = await admin.SubmitAsync(page, "receipt", new Dictionary<string, string> { ["Data.InvoiceNumber"] = "R-118" });
        Assert.Contains("Der Beleg wurde gespeichert.", saved.Text, StringComparison.Ordinal);
        Assert.Contains("R-118", saved.Text, StringComparison.Ordinal);
        Assert.NotNull(saved.Document.QuerySelector("iframe.document-preview"));
        Assert.Contains("Gartenpflege", saved.Text, StringComparison.Ordinal);
        Assert.Single(extractor.Calls);
    }

    [Fact]
    public async Task Learned_cost_type_is_proposed_on_the_next_receipt_of_the_supplier()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        await PropertyAsync();
        extractor.Suggestion = Suggestion();

        // First receipt: the user decides for "Hauswart" instead of the proposed "Gartenpflege".
        var page = await admin.SubmitWithFileAsync(await admin.GetAsync("/verwaltung/belege/neu"), "extract", "ExtractFiles", Pdf, "r1.pdf", "application/pdf");
        await admin.SubmitAsync(page, "receipt", new Dictionary<string, string> { ["Position.CostType"] = "betrkv-14" });

        // Second extraction: the model still says "Gartenpflege", but the learned rule wins.
        page = await admin.SubmitWithFileAsync(await admin.GetAsync("/verwaltung/belege/neu"), "extract", "ExtractFiles", Pdf, "r2.pdf", "application/pdf");

        Assert.Equal("betrkv-14", Selected(page, "#item-cost-type"));
        Assert.Contains("aus einem früheren Beleg dieses Lieferanten gelernt", page.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Section_35a_suggestion_opens_the_detail_fields()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        await PropertyAsync();
        extractor.Suggestion = Suggestion() with
        {
            Section35a = new Suggested<Section35a?>(Section35a.TradesmanService, 0.9),
            LaborCosts = new Suggested<decimal?>(357m, 0.9),
        };

        var page = await admin.SubmitWithFileAsync(await admin.GetAsync("/verwaltung/belege/neu"), "extract", "ExtractFiles", Pdf, "r.pdf", "application/pdf");

        Assert.Equal("TradesmanService", Selected(page, "#item-35a"));
        Assert.Equal("357", Value(page, "#item-labor").Replace(".00", "", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Failed_extraction_shows_the_message_and_keeps_the_manual_form()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        extractor.Failure = "Die Antwort des Modells konnte nicht ausgewertet werden.";

        var page = await admin.SubmitWithFileAsync(await admin.GetAsync("/verwaltung/belege/neu"), "extract", "ExtractFiles", Pdf, "r.pdf", "application/pdf");

        Assert.Contains("konnte nicht ausgewertet werden", page.Document.QuerySelector("[role=alert]")!.TextContent, StringComparison.Ordinal);
        Assert.Null(page.Document.QuerySelector(".suggestion-panel"));
        Assert.NotNull(page.Document.QuerySelector("#receipt-gross")); // manual entry still possible
        Assert.Equal("", Value(page, "#receipt-gross"));
    }

    [Fact]
    public async Task Extraction_without_file_is_refused()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;

        var page = await admin.SubmitAsync(await admin.GetAsync("/verwaltung/belege/neu"), "extract");

        Assert.Contains("mindestens eine Datei", page.Text, StringComparison.Ordinal);
        Assert.Empty(extractor.Calls);
    }

    [Fact]
    public async Task Forged_upload_token_is_ignored_and_cannot_attach_or_extract_foreign_files()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var foreign = Guid.CreateVersion7().ToString("N");
        extractor.Suggestion = Suggestion();

        var extract = await admin.SubmitAsync(await admin.GetAsync("/verwaltung/belege/neu"), "extract", new Dictionary<string, string> { ["StoredFileIds"] = foreign });
        var save = await admin.SubmitAsync(await admin.GetAsync("/verwaltung/belege/neu"), "receipt", new Dictionary<string, string>
        {
            ["StoredFileIds"] = foreign,
            ["Data.SupplierName"] = "X",
            ["Data.InvoiceDate"] = "2026-01-01",
            ["Data.Gross"] = "10",
        });

        Assert.Empty(extractor.Calls);
        Assert.Contains("mindestens eine Datei", extract.Text, StringComparison.Ordinal);
        Assert.Contains("Noch keine Datei.", save.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Existing_receipt_can_be_read_again_and_the_suggestion_is_only_applied_to_the_form()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        extractor.Suggestion = Suggestion();
        var receipt = await admin.SubmitWithFileAsync(await admin.GetAsync("/verwaltung/belege/neu"), "receipt", "Files", Pdf, "r.pdf", "application/pdf", new Dictionary<string, string>
        {
            ["Data.SupplierName"] = "Alter Name",
            ["Data.InvoiceDate"] = "2026-01-01",
            ["Data.Gross"] = "10",
        });

        var read = await admin.SubmitAsync(receipt, "extract");

        Assert.Equal("Grünpflege Meier", Value(read, "#receipt-supplier-name"));
        Assert.Equal("2026-04-30", Value(read, "#receipt-date"));
        Assert.Contains("nur ein Vorschlag", read.Text, StringComparison.Ordinal);
        Assert.Single(extractor.Calls);

        // not saved: the page still shows the original data
        var again = await admin.GetAsync(receipt.Url.AbsolutePath);
        Assert.Equal("Alter Name", Value(again, "#receipt-supplier-name"));
        Assert.Equal("2026-01-01", Value(again, "#receipt-date"));
    }

    [Fact]
    public async Task Receipt_without_file_offers_no_extraction()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        await PropertyAsync();
        var receipt = await admin.SubmitAsync(await admin.GetAsync("/verwaltung/belege/neu"), "receipt", new Dictionary<string, string>
        {
            ["Data.SupplierName"] = "Ohne Datei",
            ["Data.InvoiceDate"] = "2026-01-01",
            ["Data.Gross"] = "10",
        });

        Assert.DoesNotContain("Vorschlag aus Beleg auslesen", receipt.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Finance_permission_is_needed()
    {
        var (manager, _) = await SignedInAsAsync(Roles.Manager, [AccessRight.MasterData]);
        using var _ = manager;

        Assert.Equal("/konto/zugriff-verweigert", (await manager.GetAsync("/verwaltung/belege/neu", followRedirects: false)).RedirectLocation!.AbsolutePath);
        Assert.Equal("/konto/zugriff-verweigert", (await manager.GetAsync("/verwaltung/system/belegauslesung", followRedirects: false)).RedirectLocation!.AbsolutePath);
    }

    [Fact]
    public async Task Settings_page_shows_the_privacy_notice_for_an_external_provider()
    {
        extractor.External = true;
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;

        var page = await admin.GetAsync("/verwaltung/system/belegauslesung");

        Assert.Contains("externer Dienst", page.Document.QuerySelector("#third-party-notice")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("Claude API", page.Document.QuerySelector("#provider-name")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("bereit", page.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-", page.Text, StringComparison.Ordinal);
    }

    private static string Value(Page page, string selector) => ((IHtmlInputElement?)page.Document.QuerySelector(selector) ?? throw new InvalidOperationException(selector)).Value;

    private static string Selected(Page page, string selector) =>
        ((IHtmlSelectElement)page.Document.QuerySelector(selector)!).Options.FirstOrDefault(o => o.IsSelected)?.Value ?? "";

    private static ReceiptSuggestion Suggestion() => new()
    {
        SupplierName = new Suggested<string>("Grünpflege Meier", 0.97),
        InvoiceNumber = new Suggested<string>("R-117", 0.5),
        InvoiceDate = new Suggested<DateOnly?>(new DateOnly(2026, 4, 30), 0.98),
        Gross = new Suggested<decimal?>(595m, 0.99),
        Net = new Suggested<decimal?>(500m, 0.99),
        Vat = new Suggested<decimal?>(95m, 0.99),
        CostType = new Suggested<string>("betrkv-10", 0.6),
        Warnings = ["Der Umsatzsteuersatz (19,0 %) ist ungewöhnlich (Testwarnung: ungewöhnlich)."],
    };

    private Task<Guid> PropertyAsync() =>
        App.WithScopeAsync(async sp => (await sp.GetRequiredService<IPropertyService>().CreateAsync(
            new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, TestContext.Current.CancellationToken)).Id!.Value);

    /// <summary>Provider stand-in: returns the prepared suggestion through the real use case (checks, rules, contacts).</summary>
    private sealed class StubExtractor : IReceiptExtractor
    {
        public bool Enabled { get; set; } = true;

        public bool Ready { get; set; } = true;

        public bool External { get; set; } = true;

        public string? Failure { get; set; }

        public ReceiptSuggestion Suggestion { get; set; } = new();

        public List<int> Calls { get; } = [];

        public ExtractorInfo Info => new(
            !Enabled ? "Keiner" : External ? "Claude API" : "Ollama", "test-modell", Enabled, Ready && Enabled, External,
            External ? "Achtung: Zum Auslesen werden die Belege an einen Dritten übermittelt (Claude API)." : "Lokal.",
            Ready ? null : "Es ist kein API-Schlüssel konfiguriert.");

        public Task<ExtractorResult> ExtractAsync(IReadOnlyList<ExtractionDocument> documents, ExtractionContext context, CancellationToken cancellationToken = default)
        {
            Calls.Add(documents.Count);
            return Failure is not null
                ? throw new ExtractionException(Failure)
                : Task.FromResult(new ExtractorResult(Suggestion, new ExtractionUsage(1200, 300)));
        }
    }
}
