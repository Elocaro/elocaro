using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Accounts;
using MietHahn.Application.Properties;
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

        var page = await admin.SubmitAsync(await admin.GetAsync($"/verwaltung/objekte/{property}/afa"), "acquisition", new Dictionary<string, string>
        {
            ["Acquisition.PurchaseDate"] = "2024-07-15",
            ["Acquisition.PurchasePrice"] = "300000",
            ["Acquisition.Notary"] = "30000",
            ["Acquisition.LandSharePercent"] = "20",
            ["Acquisition.CompletionYear"] = "1990",
            ["Acquisition.SpecialYears"] = "4",
        });
        Assert.Contains("Die Anschaffungsdaten wurden gespeichert.", page.Text, StringComparison.Ordinal);
        Assert.Contains("Abschreibungsplan", page.Text, StringComparison.Ordinal);
        Assert.Contains("2.640,00", page.Text, StringComparison.Ordinal);

        page = await admin.SubmitAsync(await admin.GetAsync(page.Path), "cost", new Dictionary<string, string>
        {
            ["Cost.Date"] = "2025-03-01",
            ["Cost.Amount"] = "45000",
            ["Cost.Description"] = "Sanierung Bäder",
            ["Cost.IsMaintenance"] = "true",
        });
        Assert.Contains("Die Kosten wurden hinzugefügt.", page.Text, StringComparison.Ordinal);
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
