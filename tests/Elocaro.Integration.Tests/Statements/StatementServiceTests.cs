using System.Security.Claims;
using Elocaro.Application.Accounts;
using Elocaro.Application.Common;
using Elocaro.Application.Leases;
using Elocaro.Application.Metering;
using Elocaro.Application.OperatingCosts;
using Elocaro.Application.People;
using Elocaro.Application.Properties;
using Elocaro.Application.Receipts;
using Elocaro.Application.Statements;
using Elocaro.Application.Todos;
using Elocaro.Domain.Common;
using Elocaro.Domain.Metering;
using Elocaro.Domain.OperatingCosts;
using Elocaro.Domain.Statements;
using Elocaro.Integration.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Integration.Tests.Statements;

/// <summary>Statement wizard: checks, release (freezing), revisions with difference statement and the deadline warning on the dashboard.</summary>
public class StatementServiceTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly ClaimsPrincipal Admin = new(new ClaimsIdentity([new Claim(ClaimTypes.Role, Roles.Admin)], "test"));

    private InfrastructureEnvironment environment = null!;
    private Guid house;
    private Guid apartment;
    private Guid leaseAnna;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Period Year(int year) => Period.CalendarYear(year);

    public async ValueTask InitializeAsync()
    {
        environment = await InfrastructureEnvironment.CreateAsync(postgres);
        await using var scope = environment.Services.CreateAsyncScope();
        var properties = scope.ServiceProvider.GetRequiredService<IPropertyService>();
        var people = scope.ServiceProvider.GetRequiredService<IPersonService>();
        var leases = scope.ServiceProvider.GetRequiredService<ILeaseService>();

        house = Validate(await properties.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct));
        apartment = Validate(await properties.CreateUnitAsync(house, new UnitInput { Name = "Wohnung 1" }, Ct));
        Validate(await properties.SetUnitAreaAsync(apartment, new AreaInput { From = new DateOnly(2020, 1, 1), Area = 60m }, Ct));
        var anna = Validate(await people.CreateAsync(new PersonInput { FirstName = "Anna", LastName = "Schmidt" }, null, Ct));
        leaseAnna = Validate(await leases.CreateAsync(new LeaseCreation { RentalUnitId = apartment, Start = new DateOnly(2025, 1, 1), TenantIds = [anna], BaseRent = 500m, OperatingCostPrepayment = 100m, People = 1 }, Ct));
        Validate(await WithAsync<ICostAllocation, Result>(s => s.SetRuleAsync(house, new CostRuleInput { CostType = "betrkv-01", From = new DateOnly(2020, 1, 1), Key = AllocationKey.LivingArea }, Ct)));
    }

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    [Fact]
    public async Task Wizard_checks_costs_against_previous_year_meters_and_occupancy()
    {
        await ReceiptAsync(2025, "betrkv-01", 500m);
        await ReceiptAsync(2025, "betrkv-02", 200m);
        await ReceiptAsync(2026, "betrkv-01", 900m);
        var meter = Validate(await WithAsync<IMeterService, Result>(s => s.CreateAsync(
            house,
            new MeterCreation { Data = new MeterInput { Type = MeterType.ColdWater, Name = "Kaltwasser", Number = "W-1", AssignmentId = apartment }, InstalledOn = new DateOnly(2025, 12, 31), InitialReading = 0m },
            Ct)));

        var wizard = (await WithAsync<IStatements, StatementWizard?>(s => s.PrepareAsync(house, Year(2026), cancellationToken: Ct)))!;

        Assert.Equal(new DateOnly(2027, 12, 31), wizard.DeadlineDate);
        Assert.Null(wizard.Released);
        Assert.Contains(wizard.For(WizardStep.Costs), f => f.CostType == "betrkv-01" && f.Text.Contains("+80 %", StringComparison.Ordinal));
        Assert.Contains(wizard.For(WizardStep.Costs), f => f.CostType == "betrkv-02" && f.Text.Contains("keine Kosten erfasst", StringComparison.Ordinal));
        Assert.Contains(wizard.For(WizardStep.Meters), f => f.Severity == FindingSeverity.Warning && f.Text.Contains("Endstand zum 31.12.2026 fehlt", StringComparison.Ordinal));
        Assert.DoesNotContain(wizard.For(WizardStep.Meters), f => f.Text.Contains("Anfangsstand", StringComparison.Ordinal));
        Assert.False(wizard.HasErrors);
        Assert.True(wizard.HasWarnings);
        Assert.NotEqual(Guid.Empty, meter);

        Assert.Null(await WithAsync<IStatements, StatementWizard?>(s => s.PrepareAsync(Guid.NewGuid(), Year(2026), cancellationToken: Ct)));
    }

    [Fact]
    public async Task Vacancy_between_two_leases_is_a_warning_and_at_the_edge_only_a_note()
    {
        var leases = await WithAsync<ILeaseService, Result>(s => s.MoveOutAsync(leaseAnna, new MoveOutInput { End = new DateOnly(2026, 3, 31) }, Ct));
        Validate(leases);
        var people = await WithAsync<IPersonService, Result>(s => s.CreateAsync(new PersonInput { FirstName = "Ben", LastName = "Meier" }, null, Ct));
        Validate(await WithAsync<ILeaseService, Result>(s => s.CreateAsync(new LeaseCreation { RentalUnitId = apartment, Start = new DateOnly(2026, 5, 1), TenantIds = [people.Id!.Value], BaseRent = 500m, OperatingCostPrepayment = 100m }, Ct)));
        await ReceiptAsync(2026, "betrkv-01", 900m);

        var wizard = (await WithAsync<IStatements, StatementWizard?>(s => s.PrepareAsync(house, Year(2026), cancellationToken: Ct)))!;

        var gap = Assert.Single(wizard.For(WizardStep.Occupancy));
        Assert.Equal(FindingSeverity.Warning, gap.Severity);
        Assert.Contains("01.04.2026 bis 30.04.2026 (30 Tage)", gap.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Release_needs_acknowledged_warnings_and_is_blocked_by_errors()
    {
        await ReceiptAsync(2026, "betrkv-01", 900m);

        var withoutAcknowledgement = await WithAsync<IStatements, Result>(s => s.ReleaseAsync(house, Year(2026), false, null, Ct));
        Assert.False(withoutAcknowledgement.Succeeded);
        Assert.Contains("Hinweise geprüft", withoutAcknowledgement.Errors.Single(), StringComparison.Ordinal);

        // A consumption key without meter type is an error and blocks the release even if acknowledged.
        Validate(await WithAsync<ICostAllocation, Result>(s => s.SetRuleAsync(house, new CostRuleInput { CostType = "betrkv-01", From = new DateOnly(2026, 1, 1), Key = AllocationKey.Consumption, MeterType = MeterType.ColdWater }, Ct)));
        var blocked = await WithAsync<IStatements, Result>(s => s.ReleaseAsync(house, Year(2026), true, null, Ct));
        Assert.False(blocked.Succeeded);
        Assert.Contains("solange Angaben fehlen", blocked.Errors[0], StringComparison.Ordinal);
        Assert.Empty(await WithAsync<IStatements, IReadOnlyList<StatementSummary>>(s => s.ListAsync(house, Ct)));
    }

    [Fact]
    public async Task Released_statement_is_frozen_and_corrections_create_a_new_revision_with_difference()
    {
        environment.Clock.SetUtcNow(new DateTimeOffset(2027, 1, 20, 10, 0, 0, TimeSpan.Zero));
        await ReceiptAsync(2026, "betrkv-01", 900m);

        var firstId = Validate(await WithAsync<IStatements, Result>(s => s.ReleaseAsync(house, Year(2026), true, "Erstfassung", Ct)));
        var first = (await WithAsync<IStatements, StoredStatement?>(s => s.GetAsync(firstId, Ct)))!;
        Assert.Equal(1, first.Summary.Revision);
        Assert.True(first.Summary.Current);
        Assert.Null(first.Difference);
        Assert.Equal(900m, first.Result.TotalCosts);
        Assert.Equal(new DateTimeOffset(2027, 1, 20, 10, 0, 0, TimeSpan.Zero), first.Summary.ReleasedAt);

        // Unchanged data → no new revision.
        var unchanged = await WithAsync<IStatements, Result>(s => s.ReleaseAsync(house, Year(2026), true, "nochmal", Ct));
        Assert.Contains("nichts geändert", unchanged.Errors.Single(), StringComparison.Ordinal);

        // A new receipt arrives later: the frozen statement keeps its numbers.
        await ReceiptAsync(2026, "betrkv-01", 300m);
        var wizard = (await WithAsync<IStatements, StatementWizard?>(s => s.PrepareAsync(house, Year(2026), cancellationToken: Ct)))!;
        Assert.Equal(1200m, wizard.Preview.Result.TotalCosts);
        Assert.Equal(first.Summary.Id, wizard.Released!.Id);
        Assert.Equal(900m, (await WithAsync<IStatements, StoredStatement?>(s => s.GetAsync(firstId, Ct)))!.Result.TotalCosts);

        var withoutReason = await WithAsync<IStatements, Result>(s => s.ReleaseAsync(house, Year(2026), true, " ", Ct));
        Assert.Contains("Begründung", withoutReason.Errors.Single(), StringComparison.Ordinal);

        var secondId = Validate(await WithAsync<IStatements, Result>(s => s.ReleaseAsync(house, Year(2026), true, "Nachträgliche Rechnung", Ct)));
        var second = (await WithAsync<IStatements, StoredStatement?>(s => s.GetAsync(secondId, Ct)))!;
        Assert.Equal(2, second.Summary.Revision);
        Assert.Equal("Nachträgliche Rechnung", second.Summary.Note);
        var row = Assert.Single(second.Difference!.Leases);
        Assert.Equal("Anna Schmidt", row.Label);
        Assert.Equal(300m, row.Difference); // only Anna is a tenant: the new 300.00 € are charged to her
        Assert.Equal(300m, second.Difference.AdditionalClaims);
        Assert.False(second.ClaimsExcluded); // deadline 31.12.2027 has not passed

        var list = await WithAsync<IStatements, IReadOnlyList<StatementSummary>>(s => s.ListAsync(house, Ct));
        Assert.Equal([2, 1], list.Select(s => s.Revision));
        Assert.Equal([true, false], list.Select(s => s.Current));
    }

    [Fact]
    public async Task Correction_after_the_deadline_excludes_additional_claims()
    {
        environment.Clock.SetUtcNow(new DateTimeOffset(2027, 12, 20, 10, 0, 0, TimeSpan.Zero));
        await ReceiptAsync(2026, "betrkv-01", 900m);
        Validate(await WithAsync<IStatements, Result>(s => s.ReleaseAsync(house, Year(2026), true, null, Ct)));
        await ReceiptAsync(2026, "betrkv-01", 300m);

        environment.Clock.SetUtcNow(new DateTimeOffset(2028, 1, 10, 10, 0, 0, TimeSpan.Zero));
        var id = Validate(await WithAsync<IStatements, Result>(s => s.ReleaseAsync(house, Year(2026), true, "zu spät", Ct)));

        var stored = (await WithAsync<IStatements, StoredStatement?>(s => s.GetAsync(id, Ct)))!;
        Assert.True(stored.ClaimsExcluded);
        Assert.Equal(300m, stored.Difference!.AdditionalClaims);
    }

    [Fact]
    public async Task Dashboard_warns_before_the_deadline_and_after_it_until_the_statement_is_released()
    {
        await ReceiptAsync(2025, "betrkv-01", 500m);

        environment.Clock.SetUtcNow(new DateTimeOffset(2026, 9, 30, 10, 0, 0, TimeSpan.Zero));
        Assert.Empty(await TodosAsync());

        environment.Clock.SetUtcNow(new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.Zero));
        var reminder = Assert.Single(await TodosAsync());
        Assert.Equal("Abrechnung 2025 erstellen: Lindenweg 3", reminder.Title);
        Assert.Equal(Urgency.Info, reminder.Urgency);
        Assert.Equal(new DateOnly(2026, 12, 31), reminder.Due);
        Assert.Equal($"/verwaltung/objekte/{house}/abrechnung?jahr=2025", reminder.Link);

        environment.Clock.SetUtcNow(new DateTimeOffset(2026, 11, 25, 10, 0, 0, TimeSpan.Zero));
        Assert.Equal(Urgency.Soon, Assert.Single(await TodosAsync()).Urgency);

        environment.Clock.SetUtcNow(new DateTimeOffset(2026, 12, 20, 10, 0, 0, TimeSpan.Zero));
        Assert.Equal(Urgency.Urgent, Assert.Single(await TodosAsync()).Urgency);

        environment.Clock.SetUtcNow(new DateTimeOffset(2027, 1, 5, 10, 0, 0, TimeSpan.Zero));
        var expired = Assert.Single(await TodosAsync());
        Assert.StartsWith("Frist für die Abrechnung 2025 verstrichen", expired.Title, StringComparison.Ordinal);

        Validate(await WithAsync<IStatements, Result>(s => s.ReleaseAsync(house, Year(2025), true, null, Ct)));
        Assert.Empty(await TodosAsync());
    }

    [Fact]
    public async Task Dismissed_reminder_disappears_and_comes_back_when_shown_again()
    {
        environment.Clock.SetUtcNow(new DateTimeOffset(2027, 1, 5, 10, 0, 0, TimeSpan.Zero));
        Assert.Single(await TodosAsync());

        await WithAsync<IStatements, bool>(async s => { await s.SetReminderDismissedAsync(house, 2025, true, Ct); return true; });
        Assert.Empty(await TodosAsync());
        Assert.True((await WithAsync<IStatements, StatementWizard?>(s => s.PrepareAsync(house, Year(2025), cancellationToken: Ct)))!.ReminderDismissed);

        await WithAsync<IStatements, bool>(async s => { await s.SetReminderDismissedAsync(house, 2025, false, Ct); return true; });
        Assert.Single(await TodosAsync());
    }

    [Fact]
    public async Task Wizard_warns_about_receipts_of_the_period_that_are_not_split()
    {
        Validate(await WithAsync<IReceiptService, Result>(s => s.CreateAsync(
            new ReceiptInput { SupplierName = "Stadt Köln", InvoiceDate = new DateOnly(2026, 6, 1), Gross = 240m }, [], null, Ct)));
        Validate(await WithAsync<IReceiptService, Result>(s => s.CreateAsync(
            new ReceiptInput { SupplierName = "Anderes Jahr", InvoiceDate = new DateOnly(2024, 6, 1), Gross = 99m }, [], null, Ct)));

        var wizard = (await WithAsync<IStatements, StatementWizard?>(s => s.PrepareAsync(house, Year(2026), cancellationToken: Ct)))!;

        var warning = Assert.Single(wizard.For(WizardStep.Costs), f => f.Text.Contains("nicht vollständig auf Kostenarten", StringComparison.Ordinal));
        Assert.Equal(FindingSeverity.Warning, warning.Severity);
        Assert.Contains("240,00 €", warning.Text, StringComparison.Ordinal);
        Assert.StartsWith("1 Beleg des Zeitraums", warning.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dashboard_is_empty_for_users_without_finance_right()
    {
        environment.Clock.SetUtcNow(new DateTimeOffset(2026, 12, 20, 10, 0, 0, TimeSpan.Zero));
        await using var scope = environment.Services.CreateAsyncScope();
        var source = scope.ServiceProvider.GetServices<ITodoSource>().Single(q => q.GetType().Name == "StatementDeadlineTodos");

        Assert.Empty(await source.TodosAsync(new ClaimsPrincipal(new ClaimsIdentity([], "test")), Ct));
    }

    // ---- helpers ---------------------------------------------------------------------------------------------------

    private static Guid Validate(Result result)
    {
        Assert.True(result.Succeeded, string.Join(", ", result.Errors));
        return result.Id!.Value;
    }

    private async Task<TResult> WithAsync<TService, TResult>(Func<TService, Task<TResult>> action)
        where TService : notnull
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }

    private async Task<IReadOnlyList<TodoItem>> TodosAsync()
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetServices<ITodoSource>().Single(q => q.GetType().Name == "StatementDeadlineTodos").TodosAsync(Admin, Ct);
    }

    private async Task ReceiptAsync(int year, string costType, decimal amount) =>
        Validate(await WithAsync<IReceiptService, Result>(s => s.CreateAsync(
            new ReceiptInput { SupplierName = "Stadt Köln", InvoiceDate = new DateOnly(year, 12, 15), Gross = amount },
            [],
            new CostItemInput { CostType = costType, PropertyId = house, From = new DateOnly(year, 1, 1), To = new DateOnly(year, 12, 31) },
            Ct)));
}
