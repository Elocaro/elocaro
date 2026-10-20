using System.Text;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Accounts;
using MietHahn.Application.Properties;
using MietHahn.Domain.Common;
using MietHahn.Domain.Receipts;
using MietHahn.Infrastructure.Persistence;
using MietHahn.Web.Tests.Account;

namespace MietHahn.Web.Tests.Tax;

public class AnlageVPageTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<(Guid Property, Guid Receipt)> PropertyWithReceiptAsync()
    {
        return await App.WithScopeAsync(async sp =>
        {
            var propertyId = (await sp.GetRequiredService<IPropertyService>().CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct)).Id!.Value;
            var db = sp.GetRequiredService<MietHahnDbContext>();
            var receipt = new Receipt(new DateOnly(2026, 2, 1), 300m) { SupplierName = "Stadt Köln", PaidOn = new DateOnly(2026, 3, 1) };
            receipt.AddCostItem("betrkv-01", 300m, propertyId, Period.CalendarYear(2026));
            db.Add(receipt);
            await db.SaveChangesAsync(Ct);
            return (propertyId, receipt.Id);
        });
    }

    [Fact]
    public async Task Evaluation_shows_lines_with_drilldown_to_the_receipt_and_exports_csv()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var (property, receipt) = await PropertyWithReceiptAsync();

        var page = await admin.GetAsync($"/verwaltung/objekte/{property}/anlage-v?jahr=2026");

        Assert.Contains("Anlage V 2026", page.Text, StringComparison.Ordinal);
        Assert.Contains("Grundsteuer", page.Text, StringComparison.Ordinal);
        Assert.Contains("300,00", page.Text, StringComparison.Ordinal);
        Assert.Contains($"/verwaltung/belege/{receipt}", page.Document.QuerySelectorAll("a").Select(a => a.GetAttribute("href")), StringComparer.Ordinal);

        using var csv = await admin.Http.GetAsync(new Uri($"/verwaltung/objekte/{property}/anlage-v.csv?jahr=2026", UriKind.Relative), Ct);
        csv.EnsureSuccessStatusCode();
        var content = Encoding.UTF8.GetString(await csv.Content.ReadAsByteArrayAsync(Ct));
        Assert.Contains("Zeile;Bezeichnung;Betrag", content, StringComparison.Ordinal);
        Assert.Contains("40;Grundsteuer;300,00", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reduced_rent_shows_a_hint()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var (property, _) = await PropertyWithReceiptAsync();

        var page = await admin.GetAsync($"/verwaltung/objekte/{property}/anlage-v?jahr=2026&entgelt=40");

        Assert.Contains("Verbilligte Vermietung", page.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Line_numbers_can_be_maintained_per_year()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var (property, _) = await PropertyWithReceiptAsync();

        var page = await admin.SubmitAsync(await admin.GetAsync("/verwaltung/finanzen/anlage-v-zeilen?jahr=2026"), "mapping", new Dictionary<string, string>
        {
            ["SaveYear"] = "2026",
            ["Line-PropertyTax"] = "77",
        });
        Assert.Contains("Die Zeilennummern wurden gespeichert.", page.Text, StringComparison.Ordinal);

        Assert.Contains("77", (await admin.GetAsync($"/verwaltung/objekte/{property}/anlage-v?jahr=2026")).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_property_shows_a_hint()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;

        Assert.Contains("Objekt nicht gefunden", (await admin.GetAsync($"/verwaltung/objekte/{Guid.NewGuid()}/anlage-v?jahr=2026")).Text, StringComparison.Ordinal);
    }
}
