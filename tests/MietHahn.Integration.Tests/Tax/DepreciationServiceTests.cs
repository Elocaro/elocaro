using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Properties;
using MietHahn.Application.Tax;
using MietHahn.Integration.Tests.Infrastructure;

namespace MietHahn.Integration.Tests.Tax;

public class DepreciationServiceTests(PostgresFixture postgres) : IAsyncLifetime
{
    private InfrastructureEnvironment environment = null!;
    private Guid propertyId;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        environment = await InfrastructureEnvironment.CreateAsync(postgres);
        await using var scope = environment.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IPropertyService>()
            .CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct);
        propertyId = result.Id!.Value;
    }

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    private static AcquisitionInput Acquisition(decimal land = 20m) => new()
    {
        PurchaseDate = new DateOnly(2024, 7, 15),
        PurchasePrice = 300_000m,
        Notary = 4_000m,
        LandRegistry = 1_000m,
        TransferTax = 19_500m,
        Broker = 5_500m,
        LandSharePercent = land,
        CompletionYear = 1990,
    };

    private async Task<T> WithAsync<T>(Func<IDepreciationService, Task<T>> action)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IDepreciationService>());
    }

    [Fact]
    public async Task Without_acquisition_there_is_no_plan()
    {
        var overview = await WithAsync(s => s.GetAsync(propertyId, Ct));

        Assert.NotNull(overview);
        Assert.Null(overview.Plan);
        Assert.Null(await WithAsync(s => s.GetAsync(Guid.NewGuid(), Ct)));
    }

    [Fact]
    public async Task Saved_acquisition_yields_pro_rata_first_year_and_can_be_changed()
    {
        Assert.True((await WithAsync(s => s.SaveAcquisitionAsync(propertyId, Acquisition(), Ct))).Succeeded);

        var plan = (await WithAsync(s => s.GetAsync(propertyId, Ct)))!.Plan!;
        Assert.Equal(264_000m, plan.BuildingBase);
        Assert.Equal(2_640m, plan.Years[0].Building);

        // second save changes the existing record instead of adding one
        Assert.True((await WithAsync(s => s.SaveAcquisitionAsync(propertyId, Acquisition(land: 40m), Ct))).Succeeded);
        var changed = (await WithAsync(s => s.GetAsync(propertyId, Ct)))!;
        Assert.Equal(198_000m, changed.Plan!.BuildingBase);
        Assert.Equal(40m, changed.Acquisition!.LandSharePercent);
    }

    [Fact]
    public async Task Costs_and_assets_enter_the_plan_and_warning_is_reported()
    {
        await WithAsync(s => s.SaveAcquisitionAsync(propertyId, Acquisition(), Ct));
        var cost = await WithAsync(s => s.AddCostAsync(propertyId, new CostInput { Date = new DateOnly(2025, 3, 1), Amount = 50_000m, Description = "Sanierung Bäder", IsMaintenance = true }, Ct));
        var asset = await WithAsync(s => s.AddAssetAsync(propertyId, new AssetInput { Name = "Einbauküche", AcquisitionDate = new DateOnly(2024, 7, 1), Cost = 6_000m, UsefulLifeYears = 10 }, Ct));

        var overview = (await WithAsync(s => s.GetAsync(propertyId, Ct)))!;
        Assert.Equal(300m, overview.Plan!.Years[0].Movables);
        Assert.Contains("Anschaffungsnahe", Assert.Single(overview.Plan.Warnings), StringComparison.Ordinal);

        // Corrections in place: the kitchen costs 12,000 € over 10 years → 600 € instead of 300 €; the cost no longer counts as maintenance.
        Assert.True((await WithAsync(s => s.UpdateAssetAsync(propertyId, asset.Id!.Value, new AssetInput { Name = "Einbauküche", AcquisitionDate = new DateOnly(2024, 7, 1), Cost = 12_000m, UsefulLifeYears = 10 }, Ct))).Succeeded);
        Assert.True((await WithAsync(s => s.UpdateCostAsync(propertyId, cost.Id!.Value, new CostInput { Date = new DateOnly(2025, 3, 1), Amount = 50_000m, Description = "Sanierung Bäder", IsMaintenance = false }, Ct))).Succeeded);
        Assert.False((await WithAsync(s => s.UpdateAssetAsync(propertyId, asset.Id!.Value, new AssetInput { Name = "Einbauküche", AcquisitionDate = new DateOnly(2024, 7, 1), Cost = 12_000m, UsefulLifeYears = 0 }, Ct))).Succeeded);
        var corrected = (await WithAsync(s => s.GetAsync(propertyId, Ct)))!;
        Assert.Equal(600m, corrected.Plan!.Years[0].Movables);
        Assert.Empty(corrected.Plan.Warnings);

        Assert.True((await WithAsync(s => s.RemoveCostAsync(propertyId, cost.Id!.Value, Ct))).Succeeded);
        Assert.True((await WithAsync(s => s.RemoveAssetAsync(propertyId, asset.Id!.Value, Ct))).Succeeded);
        var cleaned = (await WithAsync(s => s.GetAsync(propertyId, Ct)))!;
        Assert.Empty(cleaned.Costs);
        Assert.Empty(cleaned.Assets);
        Assert.Empty(cleaned.Plan!.Warnings);
    }

    [Fact]
    public async Task Invalid_values_and_unknown_records_are_rejected()
    {
        var invalid = Acquisition();
        invalid.PurchasePrice = 0m;
        Assert.False((await WithAsync(s => s.SaveAcquisitionAsync(propertyId, invalid, Ct))).Succeeded);
        Assert.False((await WithAsync(s => s.SaveAcquisitionAsync(Guid.NewGuid(), Acquisition(), Ct))).Succeeded);
        Assert.False((await WithAsync(s => s.RemoveCostAsync(propertyId, Guid.NewGuid(), Ct))).Succeeded);
        Assert.False((await WithAsync(s => s.AddAssetAsync(propertyId, new AssetInput { Name = "X", AcquisitionDate = new DateOnly(2024, 1, 1), Cost = 100m, UsefulLifeYears = 0 }, Ct))).Succeeded);
    }
}
