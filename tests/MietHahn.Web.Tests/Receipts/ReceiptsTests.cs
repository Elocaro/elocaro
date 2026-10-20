using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Accounts;
using MietHahn.Application.Files;
using MietHahn.Application.Properties;
using MietHahn.Application.Receipts;
using MietHahn.Domain.Receipts;
using MietHahn.Web.Tests.Account;

namespace MietHahn.Web.Tests.Receipts;

public class ReceiptsTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    private static readonly byte[] Pdf = "%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\ntrailer << /Root 1 0 R >>\n%%EOF\n"u8.ToArray();

    // Capturing, splitting and deleting in the UI is covered by ReceiptsBrowserTests.
    [Fact]
    public async Task Split_receipt_is_listed_filtered_and_shown_with_its_cost_types()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var house = await PropertyAsync();
        var id = await App.WithScopeAsync(async sp =>
        {
            var ct = TestContext.Current.CancellationToken;
            var receipts = sp.GetRequiredService<IReceiptService>();
            await using var stream = new MemoryStream(Pdf);
            var file = await sp.GetRequiredService<IFileStore>().SaveAsync(stream, "rechnung.pdf", ct);
            var created = await receipts.CreateAsync(
                new ReceiptInput { SupplierName = "Grünpflege Meier", InvoiceNumber = "R-117", InvoiceDate = new DateOnly(2026, 4, 30), Gross = 595m, Net = 500m, Vat = 95m }, [file.Id], null, ct);
            Assert.True(created.Succeeded);
            Assert.True((await receipts.AddItemAsync(created.Id!.Value, new CostItemInput { CostType = "betrkv-14", PropertyId = house, Amount = 357m, Section35a = Section35a.HouseholdService, LaborCosts = 357m }, ct)).Succeeded);
            Assert.False((await receipts.AddItemAsync(created.Id!.Value, new CostItemInput { CostType = "betrkv-10", PropertyId = house, Amount = 300m }, ct)).Succeeded);
            Assert.True((await receipts.AddItemAsync(created.Id!.Value, new CostItemInput { CostType = "betrkv-10", PropertyId = house }, ct)).Succeeded);
            return created.Id!.Value;
        });

        var receipt = await admin.GetAsync($"/verwaltung/belege/{id}");
        Assert.DoesNotContain("nicht aufgeteilt", receipt.Text, StringComparison.Ordinal);
        Assert.Contains("Hauswart", receipt.Text, StringComparison.Ordinal);
        Assert.Contains("Gartenpflege", receipt.Text, StringComparison.Ordinal);
        Assert.Contains("595,00 €", receipt.Document.QuerySelector("tfoot")!.TextContent, StringComparison.Ordinal);
        Assert.Null(receipt.Document.QuerySelector("h3")); // fully allocated: no form for another position
        Assert.NotNull(receipt.Document.QuerySelector("iframe.document-preview"));

        var list = await admin.GetAsync("/verwaltung/belege?kostenart=betrkv-10&jahr=2026");
        Assert.Contains("Grünpflege Meier", list.Text, StringComparison.Ordinal);
        Assert.Contains("unbezahlt", list.Text, StringComparison.Ordinal);
        Assert.Contains("Keine Belege in dieser Auswahl.", (await admin.GetAsync("/verwaltung/belege?aufteilen=true")).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Finance_permission_is_needed()
    {
        var (manager, _) = await SignedInAsAsync(Roles.Manager, [AccessRight.MasterData]);
        using var _ = manager;
        Assert.Equal("/konto/zugriff-verweigert", (await manager.GetAsync("/verwaltung/belege", followRedirects: false)).RedirectLocation!.AbsolutePath);
    }

    private Task<Guid> PropertyAsync() =>
        App.WithScopeAsync(async sp => (await sp.GetRequiredService<IPropertyService>().CreateAsync(
            new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, TestContext.Current.CancellationToken)).Id!.Value);
}
