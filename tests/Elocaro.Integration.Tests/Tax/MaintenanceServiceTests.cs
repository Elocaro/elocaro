using Elocaro.Application.Properties;
using Elocaro.Application.Tax;
using Elocaro.Domain.Common;
using Elocaro.Domain.Receipts;
using Elocaro.Infrastructure.Persistence;
using Elocaro.Integration.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Integration.Tests.Tax;

public class MaintenanceServiceTests(PostgresFixture postgres) : IAsyncLifetime
{
    private InfrastructureEnvironment environment = null!;
    private Guid propertyId;
    private Guid costItemId;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        environment = await InfrastructureEnvironment.CreateAsync(postgres);
        await using var scope = environment.Services.CreateAsyncScope();
        propertyId = (await scope.ServiceProvider.GetRequiredService<IPropertyService>()
            .CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct)).Id!.Value;
        costItemId = await AddReceiptAsync(new DateOnly(2025, 3, 10));
    }

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    private async Task<Guid> AddReceiptAsync(DateOnly? paidOn)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ElocaroDbContext>();
        var receipt = new Receipt(new DateOnly(2025, 3, 1), 10_000m) { SupplierName = "Dachdecker Meier", PaidOn = paidOn };
        var item = receipt.AddCostItem(CostTypes.MaintenanceKey, 10_000m, propertyId, Period.CalendarYear(2025));
        db.Add(receipt);
        await db.SaveChangesAsync(Ct);
        return item.Id;
    }

    private async Task<T> WithAsync<T>(Func<IMaintenanceService, Task<T>> action)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IMaintenanceService>());
    }

    [Fact]
    public async Task Item_without_election_is_deducted_in_the_year_of_payment()
    {
        var deduction = await WithAsync(s => s.DeductionAsync(propertyId, 2025, Ct));

        Assert.Equal(10_000m, deduction.Total);
        Assert.Empty((await WithAsync(s => s.DeductionAsync(propertyId, 2026, Ct))).Portions);
    }

    [Fact]
    public async Task Distribution_over_four_years_deducts_2500_in_each_year()
    {
        var result = await WithAsync(s => s.DistributeAsync(propertyId, new DistributionInput { CostItemId = costItemId, Years = 4 }, Ct));
        Assert.True(result.Succeeded);

        foreach (var year in new[] { 2025, 2026, 2027, 2028 })
        {
            Assert.Equal(2_500m, (await WithAsync(s => s.DeductionAsync(propertyId, year, Ct))).Total);
        }

        Assert.Equal(0m, (await WithAsync(s => s.DeductionAsync(propertyId, 2029, Ct))).Total);
        var item = Assert.Single((await WithAsync(s => s.GetAsync(propertyId, Ct)))!.Items);
        Assert.Equal(4, item.Years);
    }

    [Fact]
    public async Task Revoked_distribution_is_deducted_immediately_again()
    {
        await WithAsync(s => s.DistributeAsync(propertyId, new DistributionInput { CostItemId = costItemId, Years = 3 }, Ct));

        Assert.True((await WithAsync(s => s.RevokeAsync(propertyId, costItemId, Ct))).Succeeded);

        Assert.Equal(10_000m, (await WithAsync(s => s.DeductionAsync(propertyId, 2025, Ct))).Total);
    }

    [Fact]
    public async Task Unpaid_item_cannot_be_distributed()
    {
        var unpaid = await AddReceiptAsync(null);

        var result = await WithAsync(s => s.DistributeAsync(propertyId, new DistributionInput { CostItemId = unpaid, Years = 3 }, Ct));

        Assert.False(result.Succeeded);
    }
}
