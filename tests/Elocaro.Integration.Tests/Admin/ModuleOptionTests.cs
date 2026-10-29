using Elocaro.Application.Admin;
using Elocaro.Application.Common;
using Elocaro.Application.Finance;
using Elocaro.Application.Leases;
using Elocaro.Application.People;
using Elocaro.Application.Properties;
using Elocaro.Domain.Leases;
using Elocaro.Integration.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Integration.Tests.Admin;

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

    [Fact]
    public async Task Deposit_stays_visible_while_a_lease_has_one()
    {
        await State.SaveAsync(Enum.GetValues<AppModule>(), completeSetup: true, [AppOption.BillingPrepayment], Ct);
        Assert.False(State.IsVisible(AppOption.LeaseDeposit));
        Assert.False(State.IsVisible(AppOption.RoomRental));

        await CreateLeaseAsync(RentModel.Standard, BillingType.Prepayment, deposit: 1500m);

        Assert.True(State.IsVisible(AppOption.LeaseDeposit));
        Assert.Equal(1, State.UsageCount(AppOption.LeaseDeposit));
        Assert.False(State.IsVisible(AppOption.LeaseGuarantor));
    }

    [Fact]
    public async Task Dunning_fees_and_interest_stay_visible_once_values_are_saved()
    {
        await State.SaveAsync(Enum.GetValues<AppModule>(), completeSetup: true, [AppOption.BillingPrepayment], Ct);
        Assert.False(State.IsVisible(AppOption.DunningFees));
        Assert.False(State.IsVisible(AppOption.DunningInterest));

        await using var scope = environment.Services.CreateAsyncScope();
        var finance = scope.ServiceProvider.GetRequiredService<IFinanceSettings>();
        var input = await finance.GetAsync(Ct);
        input.FirstNoticeFee = 5m;
        await finance.SaveAsync(input, Ct);

        Assert.True(State.IsVisible(AppOption.DunningFees));
        Assert.False(State.IsVisible(AppOption.DunningInterest));

        input.FirstNoticeFee = 0m;
        input.ChargeDefaultInterest = true;
        await finance.SaveAsync(input, Ct);

        Assert.False(State.IsVisible(AppOption.DunningFees));
        Assert.True(State.IsVisible(AppOption.DunningInterest));
    }

    private async Task<Guid> CreateLeaseAsync(RentModel model, BillingType billing, decimal? deposit = null)
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
            Data = new LeaseInput { Deposit = deposit, BillingType = billing },
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
