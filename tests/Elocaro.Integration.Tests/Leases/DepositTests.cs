using System.Security.Claims;
using Elocaro.Application.Accounts;
using Elocaro.Application.Common;
using Elocaro.Application.Leases;
using Elocaro.Application.People;
using Elocaro.Application.Properties;
using Elocaro.Application.Todos;
using Elocaro.Domain.Deposits;
using Elocaro.Integration.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Integration.Tests.Leases;

public class DepositTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly ClaimsPrincipal Administrator = new(new ClaimsIdentity([new Claim(ClaimTypes.Role, Roles.Admin)], "test"));

    private InfrastructureEnvironment environment = null!;
    private Guid lease;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        environment = await InfrastructureEnvironment.CreateAsync(postgres);
        await using var scope = environment.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var properties = sp.GetRequiredService<IPropertyService>();
        var house = Validate(await properties.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct));
        var unit = Validate(await properties.CreateUnitAsync(house, new UnitInput { Name = "Wohnung 1" }, Ct));
        var tenant = Validate(await sp.GetRequiredService<IPersonService>().CreateAsync(new PersonInput { FirstName = "Anna", LastName = "Schmidt" }, null, Ct));
        // Clock: 1 March 2026.
        lease = Validate(await sp.GetRequiredService<ILeaseService>().CreateAsync(
            new LeaseCreation { RentalUnitId = unit, Start = new DateOnly(2025, 12, 1), TenantIds = [tenant], BaseRent = 650m, Data = new LeaseInput { Deposit = 1950m, DepositNote = "Sparbuch" } }, Ct));
    }

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    [Fact]
    public async Task Payments_interest_and_refund_make_the_balance_and_a_refund_cannot_exceed_it()
    {
        Validate(await AddAsync(DepositEntryKind.Received, 650m, "Rate 1"));
        Validate(await AddAsync(DepositEntryKind.Received, 650m, "Rate 2"));
        Validate(await AddAsync(DepositEntryKind.Interest, 10m));

        var view = (await WithAsync(d => d.GetAsync(lease, Ct)))!;
        Assert.Equal(1310m, view.Account.Balance);
        Assert.Equal(650m, view.Account.Outstanding);
        Assert.Equal("Sparbuch", view.Note);
        Assert.Equal("Anna Schmidt", view.Tenant);
        Assert.Equal(3, view.Entries.Count);

        var tooMuch = await AddAsync(DepositEntryKind.Refund, 1311m);
        Assert.False(tooMuch.Succeeded);
        Assert.Contains("nur 1310,00 € Kaution", tooMuch.Errors.Single(), StringComparison.Ordinal);

        Validate(await AddAsync(DepositEntryKind.Deduction, 300m, "Nachzahlung 2025"));
        Validate(await AddAsync(DepositEntryKind.Refund, 1010m));
        Assert.Equal(0m, (await WithAsync(d => d.GetAsync(lease, Ct)))!.Account.Balance);
    }

    [Fact]
    public async Task A_deduction_without_a_reason_and_entries_for_unknown_leases_are_rejected()
    {
        Validate(await AddAsync(DepositEntryKind.Received, 650m));

        Assert.False((await AddAsync(DepositEntryKind.Deduction, 50m)).Succeeded);
        Assert.False((await WithAsync(d => d.AddAsync(Guid.NewGuid(), new DepositEntryInput { Kind = DepositEntryKind.Received, Date = new DateOnly(2026, 1, 1), Amount = 10m }, Ct))).Succeeded);
        Assert.Null(await WithAsync(d => d.GetAsync(Guid.NewGuid(), Ct)));
    }

    [Fact]
    public async Task Deleting_a_payment_is_refused_when_the_refund_would_then_exceed_the_deposit()
    {
        var payment = Validate(await AddAsync(DepositEntryKind.Received, 650m));
        Validate(await AddAsync(DepositEntryKind.Refund, 100m));

        var refused = await WithAsync(d => d.DeleteAsync(payment, Ct));

        Assert.False(refused.Succeeded);
        Assert.Contains("höher als die eingegangene Kaution", refused.Errors.Single(), StringComparison.Ordinal);
        Assert.Equal(550m, (await WithAsync(d => d.GetAsync(lease, Ct)))!.Account.Balance);

        var refund = (await WithAsync(d => d.GetAsync(lease, Ct)))!.Entries.Single(e => e.Kind == DepositEntryKind.Refund).Id;
        Validate(await WithAsync(d => d.DeleteAsync(refund, Ct)));
        Validate(await WithAsync(d => d.DeleteAsync(payment, Ct)));
        Assert.Empty((await WithAsync(d => d.GetAsync(lease, Ct)))!.Entries);
    }

    [Fact]
    public async Task Dashboard_reminds_of_the_open_deposit_after_the_third_instalment_and_of_the_refund_after_the_end()
    {
        // Lease from 1 December 2025, today 1 March 2026: the third instalment (1 February) is past, nothing paid yet.
        var open = Assert.Single(await TodosAsync());
        Assert.Equal("Kaution", open.Area);
        Assert.Contains("Kaution offen: Wohnung 1 (1950,00 €)", open.Title, StringComparison.Ordinal);
        Assert.Equal(Urgency.Soon, open.Urgency);

        Validate(await AddAsync(DepositEntryKind.Received, 1950m));
        Assert.Empty(await TodosAsync());

        // The tenant moves out at the end of March; the reminder comes three months after the end (end of June).
        await using (var scope = environment.Services.CreateAsyncScope())
        {
            Validate(await scope.ServiceProvider.GetRequiredService<ILeaseService>().MoveOutAsync(lease, new MoveOutInput { End = new DateOnly(2026, 3, 31) }, Ct));
        }

        Assert.Empty(await TodosAsync());
        environment.Clock.SetUtcNow(new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero));
        var refund = Assert.Single(await TodosAsync());
        Assert.Contains("Kaution zurückzahlen oder verrechnen: Wohnung 1 (1950,00 €)", refund.Title, StringComparison.Ordinal);
        Assert.Equal(Urgency.Info, refund.Urgency);

        Validate(await AddAsync(DepositEntryKind.Refund, 1950m));
        Assert.Empty(await TodosAsync());
    }

    private Task<Result> AddAsync(DepositEntryKind kind, decimal amount, string? note = null) =>
        WithAsync(d => d.AddAsync(lease, new DepositEntryInput { Kind = kind, Date = new DateOnly(2026, 2, 1), Amount = amount, Note = note }, Ct));

    private async Task<IReadOnlyList<TodoItem>> TodosAsync()
    {
        await using var scope = environment.Services.CreateAsyncScope();
        var source = scope.ServiceProvider.GetServices<ITodoSource>().Single(s => s.GetType().Name == "DepositTodos");
        return await source.TodosAsync(Administrator, Ct);
    }

    private static Guid Validate(Result result)
    {
        Assert.True(result.Succeeded, string.Join(", ", result.Errors));
        return result.Id!.Value;
    }

    private async Task<T> WithAsync<T>(Func<IDeposits, Task<T>> action)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IDeposits>());
    }
}
