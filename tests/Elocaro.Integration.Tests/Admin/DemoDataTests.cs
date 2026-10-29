using Elocaro.Application.Admin;
using Elocaro.Application.Finance;
using Elocaro.Application.Properties;
using Elocaro.Application.Setup;
using Elocaro.Integration.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Integration.Tests.Admin;

public class DemoDataTests(PostgresFixture postgres) : IAsyncLifetime
{
    private InfrastructureEnvironment environment = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => environment = await InfrastructureEnvironment.CreateAsync(postgres);

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    [Fact]
    public async Task Example_data_give_a_house_with_three_tenants_and_different_payment_behaviour()
    {
        Assert.True(await WithAsync(d => d.CanLoadAsync(Ct)));

        var result = await WithAsync(d => d.LoadAsync(Ct));

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        Assert.Equal(new DataImportSummary(1, 4, 3, 3, 0, 0), result.Summary);
        var house = Assert.Single(await WithAsync<IPropertyService, IReadOnlyList<PropertyOverview>>(p => p.ListAsync(null, Ct)));
        Assert.Equal(("Beispielhaus (Demo)", 4, 3), (house.Name, house.Units, house.Rented));

        var open = await WithAsync<ITenantAccounts, IReadOnlyList<OpenItem>>(a => a.OpenItemsAsync(Ct));
        var arrears = open.GroupBy(i => i.Tenant).ToDictionary(g => g.Key, g => g.Sum(i => i.Open));
        Assert.DoesNotContain(arrears, a => a.Key.Contains("Musterfrau", StringComparison.Ordinal));
        Assert.Contains(arrears, a => a.Key.Contains("Mustermann", StringComparison.Ordinal) && a.Value > 0);
        Assert.Contains(arrears, a => a.Key.Contains("Beispiel", StringComparison.Ordinal) && a.Value > 0);
        Assert.NotNull(await WithAsync<ILandlordProfileService, LandlordProfileInput?>(l => l.GetAsync(Ct)));
    }

    [Fact]
    public async Task Example_data_load_only_once_and_not_into_an_installation_with_data()
    {
        Assert.True((await WithAsync(d => d.LoadAsync(Ct))).Succeeded);

        Assert.False(await WithAsync(d => d.CanLoadAsync(Ct)));
        var again = await WithAsync(d => d.LoadAsync(Ct));
        Assert.False(again.Succeeded);
        Assert.Contains("leeren Installation", again.Errors.Single(), StringComparison.Ordinal);
        Assert.Single(await WithAsync<IPropertyService, IReadOnlyList<PropertyOverview>>(p => p.ListAsync(null, Ct)));
    }

    private async Task<T> WithAsync<T>(Func<IDemoData, Task<T>> action) => await WithAsync<IDemoData, T>(action);

    private async Task<TResult> WithAsync<TService, TResult>(Func<TService, Task<TResult>> action)
        where TService : notnull
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }
}
