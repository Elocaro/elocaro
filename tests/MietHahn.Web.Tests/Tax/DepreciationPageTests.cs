using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Accounts;
using MietHahn.Application.Properties;
using MietHahn.Application.Tax;
using MietHahn.Web.Tests.Account;

namespace MietHahn.Web.Tests.Tax;

public class DepreciationPageTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<Guid> PropertyAsync() => App.WithScopeAsync(async sp =>
        (await sp.GetRequiredService<IPropertyService>().CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct)).Id!.Value);

    [Fact]
    public async Task Acquisition_creates_plan_and_cost_over_the_limit_warns()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var property = await PropertyAsync();

        await App.WithScopeAsync(async sp => (await sp.GetRequiredService<IDepreciationService>().SaveAcquisitionAsync(property, new AcquisitionInput
        {
            PurchaseDate = new DateOnly(2024, 7, 15),
            PurchasePrice = 300_000m,
            Notary = 30_000m,
            LandSharePercent = 20m,
            CompletionYear = 1990,
            SpecialYears = 4,
        }, Ct)).Succeeded);
        var page = await admin.GetAsync($"/verwaltung/objekte/{property}/afa");
        Assert.Contains("Abschreibungsplan", page.Text, StringComparison.Ordinal);
        Assert.Contains("2.640,00", page.Text, StringComparison.Ordinal);

        await App.WithScopeAsync(async sp => (await sp.GetRequiredService<IDepreciationService>().AddCostAsync(property, new CostInput
        {
            Date = new DateOnly(2025, 3, 1),
            Amount = 45_000m,
            Description = "Sanierung Bäder",
            IsMaintenance = true,
        }, Ct)).Succeeded);
        page = await admin.GetAsync(page.Path);
        Assert.Contains("Anschaffungsnahe Herstellungskosten", page.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_property_shows_a_hint()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;

        Assert.Contains("Objekt nicht gefunden", (await admin.GetAsync($"/verwaltung/objekte/{Guid.NewGuid()}/afa")).Text, StringComparison.Ordinal);
    }
}
