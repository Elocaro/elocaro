using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Accounts;
using MietHahn.Application.Properties;
using MietHahn.Domain.Common;
using MietHahn.Domain.Receipts;
using MietHahn.Infrastructure.Persistence;
using MietHahn.Web.Tests.Account;

namespace MietHahn.Web.Tests.Tax;

public class MaintenancePageTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Maintenance_item_can_be_distributed()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var (property, item) = await App.WithScopeAsync(async sp =>
        {
            var propertyId = (await sp.GetRequiredService<IPropertyService>().CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct)).Id!.Value;
            var db = sp.GetRequiredService<MietHahnDbContext>();
            var receipt = new Receipt(new DateOnly(2025, 3, 1), 10_000m) { SupplierName = "Dachdecker Meier", PaidOn = new DateOnly(2025, 3, 10) };
            var costItem = receipt.AddCostItem(CostTypes.MaintenanceKey, 10_000m, propertyId, Period.CalendarYear(2025));
            db.Add(receipt);
            await db.SaveChangesAsync(Ct);
            return (propertyId, costItem.Id);
        });

        var page = await admin.GetAsync($"/verwaltung/objekte/{property}/erhaltungsaufwand");
        Assert.Contains("sofort abgezogen", page.Text, StringComparison.Ordinal);

        page = await admin.SubmitAsync(page, "distribution", new Dictionary<string, string>
        {
            ["DistributeId"] = item.ToString(),
            [$"Years-{item}"] = "4",
        });

        Assert.Contains("Der Erhaltungsaufwand wird verteilt.", page.Text, StringComparison.Ordinal);
        Assert.Contains("verteilt auf 4 Jahre", page.Text, StringComparison.Ordinal);
        Assert.Contains("2.500,00", page.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_property_shows_a_hint()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;

        Assert.Contains("Objekt nicht gefunden", (await admin.GetAsync($"/verwaltung/objekte/{Guid.NewGuid()}/erhaltungsaufwand")).Text, StringComparison.Ordinal);
    }
}
