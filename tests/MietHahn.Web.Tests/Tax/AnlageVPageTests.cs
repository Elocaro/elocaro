using System.Text;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Accounts;
using MietHahn.Application.Properties;
using MietHahn.Application.Tax;
using MietHahn.Domain.Common;
using MietHahn.Domain.Receipts;
using MietHahn.Domain.Tax;
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
    public async Task Evaluation_for_an_owner_shows_the_share_of_every_amount_in_page_and_csv()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var (property, _) = await PropertyWithReceiptAsync();
        var (anna, ben) = await App.WithScopeAsync(async sp =>
        {
            var people = sp.GetRequiredService<MietHahn.Application.People.IPersonService>();
            var a = (await people.CreateAsync(new MietHahn.Application.People.PersonInput { FirstName = "Anna", LastName = "Schmidt" }, null, Ct)).Id!.Value;
            var b = (await people.CreateAsync(new MietHahn.Application.People.PersonInput { FirstName = "Ben", LastName = "Weber" }, null, Ct)).Id!.Value;
            var owners = sp.GetRequiredService<IPropertyOwners>();
            await owners.AddAsync(property, new OwnerInput { PersonId = a, SharePercent = 60m, ValidFrom = new DateOnly(2020, 1, 1) }, Ct);
            await owners.AddAsync(property, new OwnerInput { PersonId = b, SharePercent = 40m, ValidFrom = new DateOnly(2020, 1, 1) }, Ct);
            return (a, b);
        });

        var page = await admin.GetAsync($"/verwaltung/objekte/{property}/anlage-v?jahr=2026&eigentuemer={anna}");
        Assert.Contains("Schmidt", page.Text, StringComparison.Ordinal);
        Assert.Contains("180,00", page.Text, StringComparison.Ordinal);

        using var csv = await admin.Http.GetAsync(new Uri($"/verwaltung/objekte/{property}/anlage-v.csv?jahr=2026&eigentuemer={ben}", UriKind.Relative), Ct);
        csv.EnsureSuccessStatusCode();
        Assert.Contains("40;Grundsteuer;120,00", Encoding.UTF8.GetString(await csv.Content.ReadAsByteArrayAsync(Ct)), StringComparison.Ordinal);

        using var stranger = await admin.Http.GetAsync(new Uri($"/verwaltung/objekte/{property}/anlage-v.csv?jahr=2026&eigentuemer={Guid.NewGuid()}", UriKind.Relative), Ct);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, stranger.StatusCode);
    }

    [Fact]
    public async Task Cheap_letting_reduces_the_expenses_after_the_local_rent_was_entered()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var (property, _) = await PropertyWithReceiptAsync();

        // Entering the rent in the form is covered by the browser tests.
        await App.WithScopeAsync(sp => sp.GetRequiredService<IAnlageVService>().SaveLocalRentAsync(property, 2026, 12_000m, Ct));
        var page = await admin.GetAsync($"/verwaltung/objekte/{property}/anlage-v?jahr=2026");

        Assert.Contains("Verbilligte Vermietung", page.Text, StringComparison.Ordinal);
        Assert.Contains("Kürzung der Werbungskosten", page.Text, StringComparison.Ordinal);
        using var csv = await admin.Http.GetAsync(new Uri($"/verwaltung/objekte/{property}/anlage-v.csv?jahr=2026", UriKind.Relative), Ct);
        Assert.Contains("Kürzung wegen verbilligter Vermietung;-300,00", Encoding.UTF8.GetString(await csv.Content.ReadAsByteArrayAsync(Ct)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Line_numbers_can_be_maintained_per_year()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var (property, _) = await PropertyWithReceiptAsync();

        await App.WithScopeAsync(sp => sp.GetRequiredService<IAnlageVService>().SaveMappingAsync(2026, new Dictionary<AnlageVCategory, int> { [AnlageVCategory.PropertyTax] = 77 }, Ct));

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
