using Elocaro.Application.Common;
using Elocaro.Application.Finance;
using Elocaro.Application.Leases;
using Elocaro.Application.People;
using Elocaro.Application.Properties;
using Elocaro.Domain.Finance;
using Elocaro.Integration.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Integration.Tests.Finance;

public class RentReductionTests(PostgresFixture postgres) : IAsyncLifetime
{
    private InfrastructureEnvironment environment = null!;
    private Guid lease;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        environment = await InfrastructureEnvironment.CreateAsync(postgres);
        await using var scope = environment.Services.CreateAsyncScope();
        var properties = scope.ServiceProvider.GetRequiredService<IPropertyService>();
        var house = Validate(await properties.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct));
        var apartment = Validate(await properties.CreateUnitAsync(house, new UnitInput { Name = "Wohnung 1" }, Ct));
        var anna = Validate(await scope.ServiceProvider.GetRequiredService<IPersonService>().CreateAsync(new PersonInput { FirstName = "Anna", LastName = "Schmidt" }, null, Ct));
        lease = Validate(await scope.ServiceProvider.GetRequiredService<ILeaseService>().CreateAsync(new LeaseCreation
        {
            RentalUnitId = apartment,
            Start = new DateOnly(2026, 3, 1),
            TenantIds = [anna],
            BaseRent = 620m,
            OperatingCostPrepayment = 150m,
        }, Ct));
        await WithAsync<IFinanceSettings, bool>(async s =>
        {
            await s.SaveAsync(new FinanceSettingsInput { AccountingStart = new DateOnly(2026, 3, 1) }, Ct);
            return true;
        });
        Now(2026, 4, 20);
        await WithAsync<ITenantAccounts, int>(k => k.GenerateChargesAsync(null, Ct));
    }

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    [Fact]
    public async Task Acknowledged_reduction_creates_a_credit_and_the_account_stays_balanced()
    {
        await PayAsync(new DateOnly(2026, 3, 3), 770m);
        Validate(await CreateAsync(RentReductionStatus.Acknowledged));
        await PayAsync(new DateOnly(2026, 4, 5), 616m);

        var account = (await WithAsync<ITenantAccounts, TenantAccount?>(k => k.GetAsync(lease, Ct)))!;
        var credit = Assert.Single(account.Entries, e => e.Kind == ChargeType.RentReduction);
        Assert.Equal("Mietminderung 04/2026 (20 %)", credit.Text);
        Assert.Equal(154m, credit.Credit);
        Assert.True(credit.Automatic);
        Assert.Equal(0m, account.Balance);
        Assert.Empty(await WithAsync<ITenantAccounts, IReadOnlyList<OpenItem>>(k => k.OpenItemsAsync(Ct)));

        // Only money that was paid counts as inflow, the credit does not.
        Assert.Equal(770m + 616m, account.AnnualValues.Sum(w => w.Inflow));
        Assert.Equal(2 * 770m, account.AnnualValues.Sum(w => w.Settled)); // credit and payments together settle all rents
    }

    [Fact]
    public async Task Credit_settles_the_rent_of_its_month_and_not_an_older_fee()
    {
        await PayAsync(new DateOnly(2026, 3, 3), 770m);
        Validate(await WithAsync<ITenantAccounts, Result>(k => k.CreateChargeAsync(lease, new ChargeInput { Kind = ChargeType.DunningFee, Due = new DateOnly(2026, 3, 20), Amount = 20m }, Ct)));
        Validate(await CreateAsync(RentReductionStatus.Acknowledged));

        // The tenant pays the reduced rent for April; the fee is still open and was not settled by the credit.
        await WithAsync<ITenantAccounts, Result>(k => k.RecordPaymentAsync(lease, new PaymentInput { Date = new DateOnly(2026, 4, 5), Amount = 616m, ChargeId = null }, Ct));
        var open = await WithAsync<ITenantAccounts, IReadOnlyList<OpenItem>>(k => k.OpenItemsAsync(Ct));

        Assert.Equal(20m, open.Sum(o => o.Open));
        var account = (await WithAsync<ITenantAccounts, TenantAccount?>(k => k.GetAsync(lease, Ct)))!;
        Assert.Equal(20m, account.Balance);
        Assert.Equal(770m + 616m - 20m, account.AnnualValues.Sum(w => w.Inflow)); // 20 € of the payments went to the fee
    }

    [Fact]
    public async Task Disputed_reduction_creates_no_credit_but_the_withheld_amount_is_not_dunned()
    {
        await PayAsync(new DateOnly(2026, 3, 3), 770m);
        var id = Validate(await CreateAsync(RentReductionStatus.Disputed));
        await PayAsync(new DateOnly(2026, 4, 5), 616m);

        var account = (await WithAsync<ITenantAccounts, TenantAccount?>(k => k.GetAsync(lease, Ct)))!;
        Assert.DoesNotContain(account.Entries, e => e.Kind == ChargeType.RentReduction);
        Assert.Equal(154m, account.Balance); // stays visible in the account

        Assert.Empty(await WithAsync<ITenantAccounts, IReadOnlyList<OpenItem>>(k => k.OpenItemsAsync(Ct)));
        var row = Assert.Single(await WithAsync<IRentReductions, IReadOnlyList<RentReductionRow>>(r => r.ForLeaseAsync(lease, Ct)));
        Assert.Equal(154m, row.DisputedTotal);
        Assert.Equal(0m, row.CreditsTotal);
        Assert.False(row.Granted);

        // Acknowledged later: the credit appears and nothing is withheld any more. Disputed again: it disappears.
        Validate(await UpdateAsync(id, RentReductionStatus.Acknowledged));
        Assert.Single((await WithAsync<ITenantAccounts, TenantAccount?>(k => k.GetAsync(lease, Ct)))!.Entries, e => e.Kind == ChargeType.RentReduction);
        Validate(await UpdateAsync(id, RentReductionStatus.Disputed));
        Assert.DoesNotContain((await WithAsync<ITenantAccounts, TenantAccount?>(k => k.GetAsync(lease, Ct)))!.Entries, e => e.Kind == ChargeType.RentReduction);
    }

    [Fact]
    public async Task Only_the_disputed_part_is_exempt_when_the_tenant_pays_less()
    {
        await PayAsync(new DateOnly(2026, 3, 3), 770m);
        var id = Validate(await CreateAsync(RentReductionStatus.Disputed));
        await PayAsync(new DateOnly(2026, 4, 5), 500m);

        var open = await WithAsync<ITenantAccounts, IReadOnlyList<OpenItem>>(k => k.OpenItemsAsync(Ct));
        Assert.Equal(116m, open.Sum(o => o.Open)); // 770 - 500 - 154 disputed
        Assert.Equal(154m, open.Sum(o => o.Disputed));

        // Acknowledged: the credit replaces the exemption, the rest stays open.
        Validate(await UpdateAsync(id, RentReductionStatus.Acknowledged));
        var acknowledged = await WithAsync<ITenantAccounts, IReadOnlyList<OpenItem>>(k => k.OpenItemsAsync(Ct));
        Assert.Equal(116m, acknowledged.Sum(o => o.Open));
        Assert.Equal(0m, acknowledged.Sum(o => o.Disputed));
    }

    [Fact]
    public async Task Changing_the_quota_adjusts_the_credit_and_ending_stops_it_and_deleting_removes_it()
    {
        var id = Validate(await CreateAsync(RentReductionStatus.Acknowledged));
        Now(2026, 6, 10);
        await WithAsync<ITenantAccounts, int>(k => k.GenerateChargesAsync(null, Ct));
        Assert.Equal([154m, 154m, 154m], Credits(await AccountAsync()));

        // Ended on 15 May: May is prorated (15/31), June has no credit.
        Validate(await UpdateAsync(id, RentReductionStatus.Ended, to: new DateOnly(2026, 5, 15)));
        Assert.Equal([154m, decimal.Round(770m * 0.2m * 15 / 31, 2)], Credits(await AccountAsync()));

        Validate(await WithAsync<IRentReductions, Result>(r => r.DeleteAsync(id, Ct)));
        Assert.Empty(Credits(await AccountAsync()));
        Assert.Empty(await WithAsync<IRentReductions, IReadOnlyList<RentReductionRow>>(r => r.ForLeaseAsync(lease, Ct)));
    }

    [Fact]
    public async Task Credits_cannot_be_edited_or_deleted_by_hand_and_the_rules_of_the_form_apply()
    {
        Validate(await CreateAsync(RentReductionStatus.Acknowledged));
        var credit = (await AccountAsync()).Entries.Single(e => e.Kind == ChargeType.RentReduction);

        Assert.False(await WithAsync<ITenantAccounts, Result>(k => k.DeleteChargeAsync(credit.Id, Ct)) is { Succeeded: true });
        Assert.False((await WithAsync<ITenantAccounts, Result>(k => k.CreateChargeAsync(lease, new ChargeInput { Kind = ChargeType.RentReduction, Due = new DateOnly(2026, 4, 3), Amount = 10m }, Ct))).Succeeded);

        var both = new RentReductionInput { Description = "Heizung", From = new DateOnly(2026, 4, 1), Percent = 10m, MonthlyAmount = 50m };
        Assert.False((await WithAsync<IRentReductions, Result>(r => r.CreateAsync(lease, both, Ct))).Succeeded);
        var neither = new RentReductionInput { Description = "Heizung", From = new DateOnly(2026, 4, 1) };
        Assert.False((await WithAsync<IRentReductions, Result>(r => r.CreateAsync(lease, neither, Ct))).Succeeded);
    }

    private Task<Result> CreateAsync(RentReductionStatus status) =>
        WithAsync<IRentReductions, Result>(r => r.CreateAsync(lease, Input(status, null), Ct));

    private Task<Result> UpdateAsync(Guid id, RentReductionStatus status, DateOnly? to = null) =>
        WithAsync<IRentReductions, Result>(r => r.UpdateAsync(id, Input(status, to), Ct));

    private static RentReductionInput Input(RentReductionStatus status, DateOnly? to) => new()
    {
        Description = "Heizung ausgefallen",
        From = new DateOnly(2026, 4, 1),
        To = to,
        Percent = 20m,
        Status = status,
    };

    private static decimal[] Credits(TenantAccount account) => [.. account.Entries.Where(e => e.Kind == ChargeType.RentReduction).OrderBy(e => e.Date).Select(e => e.Credit)];

    private Task<TenantAccount> AccountAsync() => WithAsync<ITenantAccounts, TenantAccount>(async k => (await k.GetAsync(lease, Ct))!);

    private async Task PayAsync(DateOnly date, decimal amount) =>
        Validate(await WithAsync<ITenantAccounts, Result>(k => k.RecordPaymentAsync(lease, new PaymentInput { Date = date, Amount = amount }, Ct)));

    private void Now(int year, int month, int day) => environment.Clock.SetUtcNow(new DateTimeOffset(year, month, day, 10, 0, 0, TimeSpan.Zero));

    private static Guid Validate(Result result)
    {
        Assert.True(result.Succeeded, string.Join(", ", result.Errors));
        return result.Id ?? Guid.Empty;
    }

    private async Task<TResult> WithAsync<TService, TResult>(Func<TService, Task<TResult>> action)
        where TService : notnull
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }
}
