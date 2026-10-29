using System.Net;
using Elocaro.Application.Accounts;
using Elocaro.Application.Receipts;
using Elocaro.Infrastructure.Receipts.Extraction;
using Elocaro.Web.Tests.Account;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elocaro.Web.Tests.Receipts;

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
    public async Task Receipt_without_file_offers_no_extraction()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var id = await App.WithScopeAsync(async sp => (await sp.GetRequiredService<IReceiptService>().CreateAsync(
            new ReceiptInput { SupplierName = "Ohne Datei", InvoiceDate = new DateOnly(2026, 1, 1), Gross = 10m }, [], null, TestContext.Current.CancellationToken)).Id!.Value);

        Assert.DoesNotContain("Vorschlag aus Beleg auslesen", (await admin.GetAsync($"/verwaltung/belege/{id}")).Text, StringComparison.Ordinal);
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
}
