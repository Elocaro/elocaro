using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Accounts;
using MietHahn.Application.Documents;
using MietHahn.Application.Leases;
using MietHahn.Application.People;
using MietHahn.Application.Properties;
using MietHahn.Application.Receipts;
using MietHahn.Web.Tests.Account;

namespace MietHahn.Web.Tests.OperatingCosts;

/// <summary>Statement wizard in the administration: steps, release, list and the frozen statement.</summary>
public class StatementPagesTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddScoped<IPdfGenerator, FakePdfGenerator>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Guid house;

    private async Task SeedAsync()
    {
        await App.WithScopeAsync<bool>(async sp =>
        {
            var properties = sp.GetRequiredService<IPropertyService>();
            house = (await properties.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct)).Id!.Value;
            var apartment = (await properties.CreateUnitAsync(house, new UnitInput { Name = "Wohnung 1" }, Ct)).Id!.Value;
            await properties.SetUnitAreaAsync(apartment, new AreaInput { From = new DateOnly(2020, 1, 1), Area = 60m }, Ct);
            var anna = (await sp.GetRequiredService<IPersonService>().CreateAsync(new PersonInput { FirstName = "Anna", LastName = "Schmidt", Email = "anna@example.de" }, null, Ct)).Id!.Value;
            await sp.GetRequiredService<ILeaseService>().CreateAsync(new LeaseCreation { RentalUnitId = apartment, Start = new DateOnly(2024, 1, 1), TenantIds = [anna], BaseRent = 500m, OperatingCostPrepayment = 100m }, Ct);
            var receipts = sp.GetRequiredService<IReceiptService>();
            await receipts.CreateAsync(
                new ReceiptInput { SupplierName = "Stadt Köln", InvoiceNumber = "G-1", InvoiceDate = new DateOnly(2025, 2, 1), Gross = 1000m },
                [],
                new CostItemInput { CostType = "betrkv-01", PropertyId = house, From = new DateOnly(2025, 1, 1), To = new DateOnly(2025, 12, 31) },
                Ct);
            await receipts.CreateAsync(
                new ReceiptInput { SupplierName = "Müllabfuhr", InvoiceNumber = "M-1", InvoiceDate = new DateOnly(2025, 2, 1), Gross = 200m },
                [],
                new CostItemInput { CostType = "betrkv-08", PropertyId = house, From = new DateOnly(2025, 1, 1), To = new DateOnly(2025, 12, 31) },
                Ct);
            await receipts.CreateAsync(
                new ReceiptInput { SupplierName = "Stadt Köln", InvoiceNumber = "G-2", InvoiceDate = new DateOnly(2026, 2, 1), Gross = 1500m },
                [],
                new CostItemInput { CostType = "betrkv-01", PropertyId = house, From = new DateOnly(2026, 1, 1), To = new DateOnly(2026, 12, 31) },
                Ct);
            var db = sp.GetRequiredService<MietHahn.Infrastructure.Persistence.MietHahnDbContext>();
            db.Add(new MietHahn.Domain.Admin.LandlordProfile("Andy Hahn", "Musterweg 1", "12345", "Musterstadt") { Iban = "DE02 1203 0000 0000 2020 51" });
            await db.SaveChangesAsync(Ct);
            return true;
        });
    }

    [Fact]
    public async Task Wizard_leads_through_the_steps_and_flags_missing_and_deviating_costs()
    {
        await SeedAsync();
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;

        var first = await admin.GetAsync($"/verwaltung/objekte/{house}/abrechnung?jahr=2026");
        Assert.Contains("Lindenweg 3", first.Document.QuerySelector("#step-1")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("31.12.2027", first.Document.QuerySelector("#step-1")!.TextContent, StringComparison.Ordinal);
        Assert.Equal(6, first.Document.QuerySelectorAll(".wizard-steps li").Length);
        Assert.Contains("schritt=2", first.Document.QuerySelector("#next")!.GetAttribute("href"), StringComparison.Ordinal);

        var costs = await admin.GetAsync($"/verwaltung/objekte/{house}/abrechnung?jahr=2026&schritt=2");
        var checklist = costs.Document.QuerySelector("#cost-checklist")!.TextContent;
        Assert.Contains("+50 %", checklist, StringComparison.Ordinal); // 1,000 → 1,500
        Assert.Contains("fehlt", checklist, StringComparison.Ordinal); // betrkv-08 only in the previous year
        Assert.Contains("keine Kosten erfasst", costs.Document.QuerySelector(".findings")!.TextContent, StringComparison.Ordinal);

        var calculation = await admin.GetAsync($"/verwaltung/objekte/{house}/abrechnung?jahr=2026&schritt=5");
        Assert.Contains("Anna Schmidt", calculation.Document.QuerySelector("#totals")!.TextContent, StringComparison.Ordinal);

        Assert.Contains("zwischen 2000 und 2100", (await admin.GetAsync($"/verwaltung/objekte/{house}/abrechnung?jahr=1800")).Text, StringComparison.Ordinal);
        Assert.Contains("Objekt nicht gefunden", (await admin.GetAsync($"/verwaltung/objekte/{Guid.NewGuid()}/abrechnung")).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Release_freezes_the_statement_and_a_correction_creates_a_new_revision()
    {
        await SeedAsync();
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;

        var release = await admin.GetAsync($"/verwaltung/objekte/{house}/abrechnung?jahr=2026&schritt=6");
        Assert.Contains("Ich habe die Hinweise geprüft", release.Text, StringComparison.Ordinal);

        var notConfirmed = await admin.SubmitAsync(release, "release", new Dictionary<string, string>());
        Assert.Contains("Hinweise geprüft", notConfirmed.Document.QuerySelector("[role=alert], .message-error, .error-list")?.TextContent ?? notConfirmed.Text, StringComparison.Ordinal);

        var done = await admin.SubmitAsync(release, "release", new Dictionary<string, string> { ["Release.Acknowledge"] = "true", ["Release.Note"] = "Erstfassung" });
        Assert.Contains("Die Abrechnung wurde freigegeben.", done.Text, StringComparison.Ordinal);
        Assert.Contains("Diese Fassung ist unveränderlich", done.Document.QuerySelector("#frozen")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("1.500,00 €", done.Document.QuerySelector("#cost-types")!.TextContent, StringComparison.Ordinal);
        Assert.Null(done.Document.QuerySelector("#difference"));

        var list = await admin.GetAsync($"/verwaltung/objekte/{house}/abrechnungen");
        Assert.Contains("Erstfassung", list.Document.QuerySelector("#statements")!.TextContent, StringComparison.Ordinal);

        // The wizard now points at the released statement and demands a reason for a correction.
        await App.WithScopeAsync<bool>(async sp =>
        {
            await sp.GetRequiredService<IReceiptService>().CreateAsync(
                new ReceiptInput { SupplierName = "Stadt Köln", InvoiceNumber = "G-3", InvoiceDate = new DateOnly(2026, 3, 1), Gross = 300m },
                [],
                new CostItemInput { CostType = "betrkv-01", PropertyId = house, From = new DateOnly(2026, 1, 1), To = new DateOnly(2026, 12, 31) },
                Ct);
            return true;
        });
        var correction = await admin.GetAsync($"/verwaltung/objekte/{house}/abrechnung?jahr=2026&schritt=6");
        Assert.NotNull(correction.Document.QuerySelector("#released-notice"));
        var withoutReason = await admin.SubmitAsync(correction, "release", new Dictionary<string, string> { ["Release.Acknowledge"] = "true" });
        Assert.Contains("Begründung", withoutReason.Text, StringComparison.Ordinal);

        var second = await admin.SubmitAsync(correction, "release", new Dictionary<string, string> { ["Release.Acknowledge"] = "true", ["Release.Note"] = "Nachträgliche Rechnung" });
        Assert.Contains("Fassung 2", second.Text, StringComparison.Ordinal);
        Assert.Contains("1.800,00 €", second.Document.QuerySelector("#cost-types")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("300,00 € zusätzlich zu zahlen", second.Document.QuerySelector("#difference")!.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Released_statement_is_sent_per_tenancy_with_proof_of_receipt_and_prepayment_proposal()
    {
        await SeedAsync();
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var release = await admin.GetAsync($"/verwaltung/objekte/{house}/abrechnung?jahr=2026&schritt=6");
        var detail = await admin.SubmitAsync(release, "release", new Dictionary<string, string> { ["Release.Acknowledge"] = "true" });

        var table = detail.Document.QuerySelector("#dispatches")!.TextContent;
        Assert.Contains("Anna Schmidt", table, StringComparison.Ordinal);
        Assert.Contains("nicht versendet", table, StringComparison.Ordinal);
        Assert.Contains("Gesamtübersicht", detail.Document.QuerySelector("#landlord-overview")!.TextContent, StringComparison.Ordinal);

        var previewLink = detail.Document.QuerySelector("#dispatches a[href$='vorschau.pdf']")!.GetAttribute("href")!;
        using (var pdf = await admin.Http.GetAsync(new Uri(previewLink, UriKind.Relative), TestContext.Current.CancellationToken))
        {
            Assert.Equal("application/pdf", pdf.Content.Headers.ContentType!.MediaType);
            Assert.Contains("no-store", pdf.Headers.CacheControl!.ToString(), StringComparison.Ordinal);
        }

        var leaseId = previewLink.Split('/')[^2];
        var sent = await admin.SubmitAsync(detail, "dispatch", new Dictionary<string, string> { ["Send.Channel"] = "Email", ["Send.ProposePrepayments"] = "true", ["Send.LeaseId"] = leaseId });
        Assert.Contains("Die Abrechnung wurde versendet", sent.Text, StringComparison.Ordinal);
        var status = sent.Document.QuerySelector("#dispatches")!.TextContent;
        Assert.Contains("per E-Mail am", status, StringComparison.Ordinal);
        Assert.Contains("Zugang nicht bestätigt", status, StringComparison.Ordinal);
        Assert.Contains("Schreiben herunterladen", status, StringComparison.Ordinal);
        Assert.Contains("Betriebskosten-Vorauszahlung", sent.Document.QuerySelector("#proposals")!.TextContent, StringComparison.Ordinal);

        Assert.Null(sent.Document.QuerySelector("#dispatches button[name='Send.LeaseId']")); // sent once: no second dispatch offered

        var dispatchId = sent.Document.QuerySelector("button[name='Confirmation.DispatchId']")!.GetAttribute("value")!;
        var received = await admin.SubmitAsync(sent, "receipt", new Dictionary<string, string> { ["Confirmation.DispatchId"] = dispatchId, ["Confirmation.ReceivedOn"] = DateTime.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), ["Confirmation.Note"] = "Lesebestätigung" });
        Assert.Contains("Der Zugang wurde vermerkt.", received.Text, StringComparison.Ordinal);
        Assert.Contains("Lesebestätigung", received.Document.QuerySelector("#dispatches")!.TextContent, StringComparison.Ordinal);

        var applyId = received.Document.QuerySelector("button[name='Decision.ApplyId']")!.GetAttribute("value")!;
        var applied = await admin.SubmitAsync(received, "proposal", new Dictionary<string, string> { ["Decision.ApplyId"] = applyId });
        Assert.Contains("Die neue Vorauszahlung wurde übernommen.", applied.Text, StringComparison.Ordinal);
        Assert.Contains("übernommen am", applied.Document.QuerySelector("#proposals")!.TextContent, StringComparison.Ordinal);
    }
}
