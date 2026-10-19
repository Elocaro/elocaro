using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Accounts;
using MietHahn.Application.Properties;
using MietHahn.Web.Tests.Account;

namespace MietHahn.Web.Tests.Receipts;

public class ReceiptsTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    private static readonly byte[] Pdf = "%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\ntrailer << /Root 1 0 R >>\n%%EOF\n"u8.ToArray();

    [Fact]
    public async Task Upload_invoice_and_split_into_two_cost_types()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var house = await PropertyAsync();

        var receipt = await admin.SubmitWithFileAsync(await admin.GetAsync("/verwaltung/belege/neu"), "receipt", "Files", Pdf, "rechnung.pdf", "application/pdf", new Dictionary<string, string>
        {
            ["Data.SupplierName"] = "Grünpflege Meier",
            ["Data.InvoiceNumber"] = "R-117",
            ["Data.InvoiceDate"] = "2026-04-30",
            ["Data.Gross"] = "595.00",
            ["Data.Net"] = "500",
            ["Data.Vat"] = "95",
        });
        Assert.Contains("Der Beleg wurde gespeichert.", receipt.Text, StringComparison.Ordinal);
        Assert.Contains("Noch 595,00 € nicht aufgeteilt", receipt.Text, StringComparison.Ordinal);
        Assert.NotNull(receipt.Document.QuerySelector("iframe.document-preview"));

        receipt = await admin.SubmitAsync(receipt, "item", new Dictionary<string, string>
        {
            ["Position.CostType"] = "betrkv-14",
            ["Position.PropertyId"] = house.ToString(),
            ["Position.Amount"] = "357",
            ["Position.Section35a"] = "HouseholdService",
            ["Position.LaborCosts"] = "357",
        });
        Assert.Contains("Die Position wurde hinzugefügt.", receipt.Text, StringComparison.Ordinal);
        Assert.Contains("Noch 238,00 € nicht aufgeteilt", receipt.Text, StringComparison.Ordinal);

        var tooMuch = await admin.SubmitAsync(receipt, "item", new Dictionary<string, string>
        {
            ["Position.CostType"] = "betrkv-10",
            ["Position.PropertyId"] = house.ToString(),
            ["Position.Amount"] = "300",
        });
        Assert.Contains("übersteigen den Rechnungsbetrag", tooMuch.Text, StringComparison.Ordinal);

        receipt = await admin.SubmitAsync(receipt, "item", new Dictionary<string, string>
        {
            ["Position.CostType"] = "betrkv-10",
            ["Position.PropertyId"] = house.ToString(),
        });
        Assert.DoesNotContain("nicht aufgeteilt", receipt.Text, StringComparison.Ordinal);
        Assert.Contains("Hauswart", receipt.Text, StringComparison.Ordinal);
        Assert.Contains("Gartenpflege", receipt.Text, StringComparison.Ordinal);
        Assert.Contains("595,00 €", receipt.Document.QuerySelector("tfoot")!.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Position hinzufügen", receipt.Text, StringComparison.Ordinal); // fully allocated

        var list = await admin.GetAsync("/verwaltung/belege?kostenart=betrkv-10&jahr=2026");
        Assert.Contains("Grünpflege Meier", list.Text, StringComparison.Ordinal);
        Assert.Contains("unbezahlt", list.Text, StringComparison.Ordinal);
        Assert.Contains("Keine Belege in dieser Auswahl.", (await admin.GetAsync("/verwaltung/belege?aufteilen=true")).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_and_delete_receipt_with_a_single_cost_type()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var house = await PropertyAsync();

        var receipt = await admin.SubmitAsync(await admin.GetAsync("/verwaltung/belege/neu"), "receipt", new Dictionary<string, string>
        {
            ["Data.SupplierName"] = "Stadt Köln",
            ["Data.InvoiceDate"] = "2026-02-15",
            ["Data.Gross"] = "480",
            ["Data.PaidOn"] = "2026-02-15",
            ["Position.CostType"] = "betrkv-01",
            ["Position.PropertyId"] = house.ToString(),
        });
        Assert.Contains("Laufende öffentliche Lasten (Grundsteuer)", receipt.Text, StringComparison.Ordinal);
        Assert.Contains("01.01.2026 – 31.12.2026", receipt.Text, StringComparison.Ordinal);
        Assert.Contains("Noch keine Datei.", receipt.Text, StringComparison.Ordinal);

        var list = await admin.SubmitAsync(receipt, "delete");
        Assert.Contains("Der Beleg wurde gelöscht.", list.Text, StringComparison.Ordinal);
        Assert.Contains("Keine Belege in dieser Auswahl.", list.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Required_fields_and_finance_permission()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var errors = await admin.SubmitAsync(await admin.GetAsync("/verwaltung/belege/neu"), "receipt", new Dictionary<string, string> { ["Data.Gross"] = "10" });
        Assert.Contains("Bitte das Rechnungsdatum angeben.", errors.Text, StringComparison.Ordinal);

        var (manager, _) = await SignedInAsAsync(Roles.Manager, [AccessRight.MasterData]);
        using var __ = manager;
        Assert.Equal("/konto/zugriff-verweigert", (await manager.GetAsync("/verwaltung/belege", followRedirects: false)).RedirectLocation!.AbsolutePath);
    }

    private Task<Guid> PropertyAsync() =>
        App.WithScopeAsync(async sp => (await sp.GetRequiredService<IPropertyService>().CreateAsync(
            new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, TestContext.Current.CancellationToken)).Id!.Value);
}
