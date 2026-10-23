using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Admin;
using MietHahn.Application.Common;
using MietHahn.Application.Leases;
using MietHahn.Application.People;
using MietHahn.Application.Properties;
using MietHahn.Domain.Leases;
using MietHahn.Integration.Tests.Infrastructure;

namespace MietHahn.Integration.Tests.Admin;

public class ModuleOptionTests(PostgresFixture postgres) : IAsyncLifetime
{
    private InfrastructureEnvironment environment = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => environment = await InfrastructureEnvironment.CreateAsync(postgres);

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    private IModuleState State => environment.Services.GetRequiredService<IModuleState>();

    private static readonly AppOption[] OnlyFixedRentWithPrepayment = [AppOption.BillingPrepayment];

    [Fact]
    public async Task Options_are_all_on_until_the_landlord_chooses()
    {
        await State.EnsureLoadedAsync(Ct);

        Assert.All(Enum.GetValues<AppOption>(), option => Assert.True(State.IsVisible(option)));
    }

    [Fact]
    public async Task A_switched_off_option_is_hidden_and_saving_modules_alone_leaves_options_untouched()
    {
        await State.SaveAsync(Enum.GetValues<AppModule>(), completeSetup: true, OnlyFixedRentWithPrepayment, Ct);

        Assert.False(State.IsVisible(AppOption.RentModelIndex));
        Assert.False(State.IsVisible(AppOption.BillingFlatRate));
        Assert.True(State.IsVisible(AppOption.BillingPrepayment));

        await State.SaveAsync(Enum.GetValues<AppModule>(), completeSetup: false, cancellationToken: Ct);

        Assert.False(State.IsVisible(AppOption.RentModelIndex));
    }

    [Fact]
    public async Task An_option_stays_visible_while_a_lease_uses_it()
    {
        await State.SaveAsync(Enum.GetValues<AppModule>(), completeSetup: true, OnlyFixedRentWithPrepayment, Ct);
        Assert.False(State.IsVisible(AppOption.RentModelIndex));

        var leaseId = await CreateLeaseAsync(RentModel.Index, BillingType.FlatRate);

        Assert.False(State.IsOptionEnabled(AppOption.RentModelIndex));
        Assert.True(State.IsVisible(AppOption.RentModelIndex));
        Assert.Equal(1, State.UsageCount(AppOption.RentModelIndex));
        Assert.True(State.IsVisible(AppOption.BillingFlatRate));
        Assert.Equal(0, State.UsageCount(AppOption.RentModelGraduated));

        await using var scope = environment.Services.CreateAsyncScope();
        Validate(await scope.ServiceProvider.GetRequiredService<ILeaseService>().SetRentModelAsync(leaseId, new RentModelInput { Model = RentModel.Standard }, Ct));

        Assert.False(State.IsVisible(AppOption.RentModelIndex));
    }

    private async Task<Guid> CreateLeaseAsync(RentModel model, BillingType billing)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        var properties = scope.ServiceProvider.GetRequiredService<IPropertyService>();
        var property = Validate(await properties.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct));
        var apartment = Validate(await properties.CreateUnitAsync(property, new UnitInput { Name = "Wohnung 1" }, Ct));
        var tenant = Validate(await scope.ServiceProvider.GetRequiredService<IPersonService>().CreateAsync(new PersonInput { FirstName = "Anna", LastName = "Schmidt" }, null, Ct));
        return Validate(await scope.ServiceProvider.GetRequiredService<ILeaseService>().CreateAsync(new LeaseCreation
        {
            RentalUnitId = apartment,
            Start = new DateOnly(2026, 3, 1),
            TenantIds = [tenant],
            BaseRent = 650m,
            BillingType = billing,
            OperatingCostFlatRate = billing == BillingType.FlatRate ? 40m : null,
            RentModel = model,
            IndexBaseMonth = model == RentModel.Index ? new DateOnly(2026, 1, 1) : null,
            IndexBaseValue = model == RentModel.Index ? 117.4m : null,
        }, Ct));
    }

    private static Guid Validate(Result result)
    {
        Assert.True(result.Succeeded, string.Join(", ", result.Errors));
        return result.Id!.Value;
    }
}
