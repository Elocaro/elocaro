using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Accounts;
using MietHahn.Application.Common;
using MietHahn.Application.Finance;
using MietHahn.Application.Leases;
using MietHahn.Application.People;
using MietHahn.Application.Properties;
using MietHahn.Application.Todos;
using MietHahn.Domain.Finance;
using MietHahn.Domain.Leases;
using MietHahn.Infrastructure.Persistence;
using MietHahn.Integration.Tests.Infrastructure;

namespace MietHahn.Integration.Tests.Finance;

public class TenantAccountsTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly ClaimsPrincipal Admin = new(new ClaimsIdentity([new Claim(ClaimTypes.Role, Roles.Admin)], "test"));

    private InfrastructureEnvironment environment = null!;
    private Guid apartment;
    private Guid lease;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        environment = await InfrastructureEnvironment.CreateAsync(postgres);
        await using var scope = environment.Services.CreateAsyncScope();
        var properties = scope.ServiceProvider.GetRequiredService<IPropertyService>();
        var house = Validate(await properties.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct));
        apartment = Validate(await properties.CreateUnitAsync(house, new UnitInput { Name = "Wohnung 1" }, Ct));
        var anna = Validate(await scope.ServiceProvider.GetRequiredService<IPersonService>().CreateAsync(new PersonInput { FirstName = "Anna", LastName = "Schmidt" }, null, Ct));

        // Einzug am 15.03.2026
        lease = Validate(await scope.ServiceProvider.GetRequiredService<ILeaseService>().CreateAsync(new LeaseCreation
        {
            RentalUnitId = apartment,
            Start = new DateOnly(2026, 3, 15),
            TenantIds = [anna],
            BaseRent = 620m,
            OperatingCostPrepayment = 150m,
        }, Ct));
    }

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    [Fact]
    public async Task Monthly_rents_prorated_from_move_in_and_no_duplicates_on_rerun()
    {
        await AccountingStartAsync(new DateOnly(2026, 1, 1));
        Now(2026, 5, 10);

        Assert.Equal(6, await WithAsync(k => k.GenerateChargesAsync(null, Ct)));
        Assert.Equal(0, await WithAsync(k => k.GenerateChargesAsync(null, Ct)));

        var account = (await WithAsync(k => k.GetAsync(lease, Ct)))!;
        var march = account.Entries.Where(b => b.Text.Contains("03/2026", StringComparison.Ordinal)).ToList();
        Assert.Equal([340.00m, 82.26m], march.Select(b => b.Debit)); // 17/31
        Assert.All(march, b => Assert.Equal(new DateOnly(2026, 3, 15), b.Date)); // fällig frühestens am Einzugstag
        Assert.Equal("Kaltmiete 03/2026 (anteilig 17/31)", march[0].Text);
        Assert.Equal(new DateOnly(2026, 4, 7), account.Entries.First(b => b.Text.StartsWith("Kaltmiete 04", StringComparison.Ordinal)).Date); // 3. Werktag nach Karfreitag und Ostermontag
        Assert.Equal(new DateOnly(2026, 5, 6), account.Entries[^1].Date); // 1.5. ist Feiertag
        Assert.Equal(340m + 82.26m + 2 * 770m, account.Balance);
        Assert.Equal(account.Balance, account.Due);
    }

    [Fact]
    public async Task Lease_changes_are_propagated()
    {
        await AccountingStartAsync(new DateOnly(2026, 1, 1));
        Now(2026, 7, 10);
        await WithAsync(k => k.GenerateChargesAsync(null, Ct));

        // Rückwirkende Mieterhöhung ab 01.06. und Auszug zum 15.07.
        await using (var scope = environment.Services.CreateAsyncScope())
        {
            var leases = scope.ServiceProvider.GetRequiredService<ILeaseService>();
            Validate(await leases.SetComponentAsync(lease, new ComponentInput { Kind = RentComponentType.BaseRent, From = new DateOnly(2026, 6, 1), Amount = 650m }, Ct));
            Validate(await leases.MoveOutAsync(lease, new MoveOutInput { End = new DateOnly(2026, 7, 15) }, Ct));
        }

        Assert.Equal(3, await WithAsync(k => k.GenerateChargesAsync(lease, Ct))); // Kalt 06 und 07, BK 07

        var account = (await WithAsync(k => k.GetAsync(lease, Ct)))!;
        Assert.Equal(650m, account.Entries.Single(b => b.Text == "Kaltmiete 06/2026").Debit);
        Assert.Equal(decimal.Round(650m * 15 / 31, 2), account.Entries.Single(b => b.Text.StartsWith("Kaltmiete 07/2026", StringComparison.Ordinal)).Debit);

        // Späterer Buchhaltungsbeginn entfernt frühere Monatsmieten.
        await AccountingStartAsync(new DateOnly(2026, 6, 1));
        await WithAsync(k => k.GenerateChargesAsync(null, Ct));
        Assert.DoesNotContain((await WithAsync(k => k.GetAsync(lease, Ct)))!.Entries, b => b.Text.Contains("05/2026", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Without_accounting_start_no_monthly_rents_but_hint_on_dashboard()
    {
        Now(2026, 5, 10);

        Assert.Equal(0, await WithAsync(k => k.GenerateChargesAsync(null, Ct)));

        var todo = Assert.Single(await FinanceTodosAsync());
        Assert.Equal("Buchhaltungsbeginn festlegen", todo.Title);
    }

    [Fact]
    public async Task Tenant_account_shows_paid_prepayments_per_year_by_inflow()
    {
        await AccountingStartAsync(new DateOnly(2026, 11, 1));
        Now(2027, 2, 10);
        await WithAsync(k => k.GenerateChargesAsync(null, Ct));

        // November pünktlich, Dezember erst im Januar, Januar zur Hälfte, Februar offen
        await PaymentAsync(new DateOnly(2026, 11, 3), 770m);
        await PaymentAsync(new DateOnly(2027, 1, 4), 770m);
        await PaymentAsync(new DateOnly(2027, 1, 20), 385m);

        var account = (await WithAsync(k => k.GetAsync(lease, Ct)))!;

        var oc2026 = account.AnnualValues.Single(w => w.Year == 2026 && w.Component == RentComponentType.OperatingCostPrepayment);
        Assert.Equal((300m, 300m, 150m), (oc2026.Debit, oc2026.Settled, oc2026.Inflow));
        var oc2027 = account.AnnualValues.Single(w => w.Year == 2027 && w.Component == RentComponentType.OperatingCostPrepayment);
        Assert.Equal((300m, 75m, 225m), (oc2027.Debit, oc2027.Settled, oc2027.Inflow));
        Assert.Equal(385m + 770m, account.Balance);
        Assert.Equal(account.Balance, account.Entries[^1].Balance);

        var open = await WithAsync(k => k.OpenItemsAsync(Ct));
        Assert.Equal(385m + 770m, open.Sum(p => p.Open));
        Assert.Equal(["Kaltmiete 01/2027", "Betriebskostenvorauszahlung 01/2027", "Kaltmiete 02/2027", "Betriebskostenvorauszahlung 02/2027"], open.Select(p => p.Text));
        Assert.Equal("Lindenweg 3, Wohnung 1", open[0].RentalUnit);
        Assert.Equal("Anna Schmidt", open[0].Tenant);
        Assert.Equal(new DateOnly(2027, 2, 10).DayNumber - new DateOnly(2027, 1, 6).DayNumber, open[0].DaysOverdue);

        var todo = Assert.Single(await FinanceTodosAsync());
        Assert.StartsWith("Rückstände: 1.155,00 € bei 1 Mietverhältnis", todo.Title, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dunning_fee_is_settled_first_under_statutory_order()
    {
        await AccountingStartAsync(new DateOnly(2026, 4, 1));
        Now(2026, 4, 20);
        await WithAsync(k => k.GenerateChargesAsync(null, Ct));
        var fee = Validate(await WithAsync(k => k.CreateChargeAsync(lease, new ChargeInput { Kind = ChargeType.DunningFee, Due = new DateOnly(2026, 4, 15), Amount = 5m }, Ct)));
        await PaymentAsync(new DateOnly(2026, 4, 18), 770m);

        var account = (await WithAsync(k => k.GetAsync(lease, Ct)))!;
        Assert.Equal(0m, account.Entries.Single(b => b.Id == fee).Open);
        Assert.Equal(5m, account.Due);
        Assert.Equal("Mahngebühr", account.Entries.Single(b => b.Id == fee).Text);

        await using (var scope = environment.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IFinanceSettings>().SaveAsync(new FinanceSettingsInput { AccountingStart = new DateOnly(2026, 4, 1), Allocation = AllocationOrder.RentFirst }, Ct);
        }

        account = (await WithAsync(k => k.GetAsync(lease, Ct)))!;
        Assert.Equal(5m, account.Entries.Single(b => b.Id == fee).Open);
    }

    [Fact]
    public async Task Refund_and_payout_offset_each_other()
    {
        await AccountingStartAsync(new DateOnly(2026, 4, 1));
        Now(2026, 4, 20);
        await WithAsync(k => k.GenerateChargesAsync(null, Ct));
        await PaymentAsync(new DateOnly(2026, 4, 3), 770m);
        var refund = Validate(await WithAsync(k => k.CreateChargeAsync(lease, new ChargeInput { Kind = ChargeType.Refund, Due = new DateOnly(2026, 4, 10), Amount = 120m, Name = "Guthaben NK 2025" }, Ct)));

        var account = (await WithAsync(k => k.GetAsync(lease, Ct)))!;
        Assert.Equal(-120m, account.Balance);
        Assert.Equal(120m, account.Entries.Single(b => b.Id == refund).Credit);

        Validate(await WithAsync(k => k.RecordPaymentAsync(lease, new PaymentInput { Date = new DateOnly(2026, 4, 15), Amount = 120m, Payout = true, Purpose = "Guthaben überwiesen" }, Ct)));
        account = (await WithAsync(k => k.GetAsync(lease, Ct)))!;
        Assert.Equal(0m, account.Balance);
        Assert.Equal("Auszahlung: Guthaben überwiesen", account.Entries[^1].Text);
        Assert.Equal(120m, account.Entries[^1].Debit);
        Assert.Empty(await WithAsync(k => k.OpenItemsAsync(Ct)));
    }

    [Fact]
    public async Task Delete_only_for_manual_entries_and_allocation_designation_is_dropped()
    {
        await AccountingStartAsync(new DateOnly(2026, 4, 1));
        Now(2026, 4, 20);
        await WithAsync(k => k.GenerateChargesAsync(null, Ct));
        var account = (await WithAsync(k => k.GetAsync(lease, Ct)))!;
        var rent = account.Entries.First(b => b.Automatic).Id;

        var rejected = await WithAsync(k => k.DeleteChargeAsync(rent, Ct));
        Assert.False(rejected.Succeeded);
        Assert.Contains("ergeben sich aus dem Vertrag", rejected.Errors.Single(), StringComparison.Ordinal);

        var deposit = Validate(await WithAsync(k => k.CreateChargeAsync(lease, new ChargeInput { Kind = ChargeType.Deposit, Due = new DateOnly(2026, 3, 15), Amount = 1860m }, Ct)));
        var payment = Validate(await WithAsync(k => k.RecordPaymentAsync(lease, new PaymentInput { Date = new DateOnly(2026, 4, 1), Amount = 1860m, ChargeId = deposit }, Ct)));
        Assert.Equal(0m, (await WithAsync(k => k.GetAsync(lease, Ct)))!.Entries.Single(b => b.Id == deposit).Open);

        Validate(await WithAsync(k => k.DeleteChargeAsync(deposit, Ct)));
        await using (var scope = environment.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MietHahnDbContext>();
            Assert.Null((await db.Set<Payment>().SingleAsync(z => z.Id == payment, Ct)).ChargeId);
        }

        Validate(await WithAsync(k => k.DeletePaymentAsync(payment, Ct)));
        Assert.Equal(770m, (await WithAsync(k => k.GetAsync(lease, Ct)))!.Balance);

        var foreign = await WithAsync(k => k.RecordPaymentAsync(lease, new PaymentInput { Date = new DateOnly(2026, 4, 1), Amount = 10m, ChargeId = Guid.CreateVersion7() }, Ct));
        Assert.False(foreign.Succeeded);
    }

    private void Now(int year, int month, int day) => environment.Clock.SetUtcNow(new DateTimeOffset(year, month, day, 10, 0, 0, TimeSpan.Zero));

    private async Task AccountingStartAsync(DateOnly from)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IFinanceSettings>().SaveAsync(new FinanceSettingsInput { AccountingStart = from }, Ct);
    }

    private async Task PaymentAsync(DateOnly date, decimal amount) =>
        Validate(await WithAsync(k => k.RecordPaymentAsync(lease, new PaymentInput { Date = date, Amount = amount }, Ct)));

    private async Task<IReadOnlyList<TodoItem>> FinanceTodosAsync()
    {
        await using var scope = environment.Services.CreateAsyncScope();
        var source = scope.ServiceProvider.GetServices<ITodoSource>().Single(q => q.GetType().Name == "FinanceTodos");
        return await source.TodosAsync(Admin, Ct);
    }

    private static Guid Validate(Result result)
    {
        Assert.True(result.Succeeded, string.Join(", ", result.Errors));
        return result.Id!.Value;
    }

    private async Task<T> WithAsync<T>(Func<ITenantAccounts, Task<T>> action)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<ITenantAccounts>());
    }
}
