using Elocaro.Application.Audit;
using Elocaro.Application.Common;
using Elocaro.Application.Leases;
using Elocaro.Application.Metering;
using Elocaro.Application.People;
using Elocaro.Application.Properties;
using Elocaro.Domain.Metering;
using Elocaro.Domain.People;
using Elocaro.Domain.Properties;
using Elocaro.Integration.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Integration.Tests.Audit;

public class UndoTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(InfrastructureEnvironment.StartTime.UtcDateTime);

    private InfrastructureEnvironment environment = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => environment = await InfrastructureEnvironment.CreateAsync(postgres);

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    [Fact]
    public async Task A_created_person_with_address_is_taken_back_in_one_step()
    {
        var id = await CreatePersonAsync("anlegen", "anna@example.de");

        var undone = await UndoAsync("anlegen");

        Assert.True(undone.Succeeded, string.Join(" ", undone.Errors));
        Assert.Null(await InScopeAsync<IPersonService, PersonDetails?>(s => s.GetAsync(id, Ct)));
    }

    [Fact]
    public async Task A_change_gets_the_earlier_values_back_and_the_undo_is_logged_as_its_own_operation()
    {
        var id = await CreatePersonAsync("anlegen", "anna@example.de");
        await ChangeEmailAsync(id, "ändern", "neu@example.de");

        var undone = await UndoAsync("ändern");

        Assert.True(undone.Succeeded, string.Join(" ", undone.Errors));
        Assert.Equal("anna@example.de", (await InScopeAsync<IPersonService, PersonDetails?>(s => s.GetAsync(id, Ct)))!.Data.Email);
        var rows = await InScopeAsync<IActivityLog, IReadOnlyList<ActivityRow>>(a => a.RecentAsync(null, 10, Ct));
        var change = rows.Single(r => r.Label == "ändern");
        Assert.True(change.Undone);
        Assert.Contains(rows, r => r.TakesBack == change.Id && r.Label == "Rückgängig: ändern");
    }

    [Fact]
    public async Task A_later_change_of_the_same_record_stops_the_undo()
    {
        var id = await CreatePersonAsync("anlegen", "anna@example.de");
        await ChangeEmailAsync(id, "erste", "eins@example.de");
        await ChangeEmailAsync(id, "zweite", "zwei@example.de");

        var check = await CheckAsync("erste");

        Assert.False(check.CanUndo);
        Assert.Contains("Person", check.Reason);
        Assert.Equal("zwei@example.de", (await InScopeAsync<IPersonService, PersonDetails?>(s => s.GetAsync(id, Ct)))!.Data.Email);
    }

    [Fact]
    public async Task A_deleted_person_is_restored()
    {
        var id = await CreatePersonAsync("anlegen", "anna@example.de");
        Assert.True((await InOperationAsync<IPersonService, Result>("löschen", s => s.DeleteAsync(id, Ct))).Succeeded);
        Assert.Null(await InScopeAsync<IPersonService, PersonDetails?>(s => s.GetAsync(id, Ct)));

        var undone = await UndoAsync("löschen");

        Assert.True(undone.Succeeded, string.Join(" ", undone.Errors));
        Assert.NotNull(await InScopeAsync<IPersonService, PersonDetails?>(s => s.GetAsync(id, Ct)));
    }

    [Fact]
    public async Task A_property_with_a_unit_cannot_be_taken_back_until_the_unit_is()
    {
        var propertyId = (await InOperationAsync<IPropertyService, Result>("Objekt", s => s.CreateAsync(
            new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln", YearBuilt = 1965, HeatingType = HeatingType.CentralHeating, EnergySource = EnergySource.NaturalGas }, Ct))).Id!.Value;
        Assert.True((await InOperationAsync<IPropertyService, Result>("Einheit", s => s.CreateUnitAsync(propertyId, new UnitInput { Name = "Wohnung 1", Location = "EG" }, Ct))).Succeeded);

        var blocked = await CheckAsync("Objekt");
        Assert.False(blocked.CanUndo);
        Assert.Contains("Unit", blocked.Reason);

        Assert.True((await UndoAsync("Einheit")).Succeeded);
        Assert.True((await UndoAsync("Objekt")).Succeeded);
        Assert.Null(await InScopeAsync<IPropertyService, PropertyDetails?>(s => s.GetAsync(propertyId, Ct)));
    }

    [Fact]
    public async Task An_operation_that_touched_bank_details_cannot_be_taken_back()
    {
        var id = await CreatePersonAsync("anlegen", "anna@example.de");
        Assert.True((await InOperationAsync<IPersonService, Result>("IBAN", s => s.AddBankDetailsAsync(id, new BankDetailsInput { Iban = "DE02120300000000202051" }, Ct))).Succeeded);

        var check = await CheckAsync("IBAN");

        Assert.False(check.CanUndo);
        Assert.Contains("BankDetails", check.Reason);
    }

    [Fact]
    public async Task An_operation_can_be_taken_back_only_once_and_an_undo_not_at_all()
    {
        await CreatePersonAsync("anlegen", "anna@example.de");
        Assert.True((await UndoAsync("anlegen")).Succeeded);

        Assert.False((await CheckAsync("anlegen")).CanUndo);
        Assert.False((await CheckAsync("Rückgängig: anlegen")).CanUndo);
    }

    private async Task<Guid> CreatePersonAsync(string label, string email) =>
        (await InOperationAsync<IPersonService, Result>(label, s => s.CreateAsync(
            new PersonInput { Salutation = Salutation.Mrs, FirstName = "Anna", LastName = "Schmidt", Email = email },
            new AddressInput { From = Today, Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct))).Id!.Value;

    private async Task ChangeEmailAsync(Guid id, string label, string email)
    {
        var data = (await InScopeAsync<IPersonService, PersonDetails?>(s => s.GetAsync(id, Ct)))!.Data;
        data.Email = email;
        Assert.True((await InOperationAsync<IPersonService, Result>(label, s => s.UpdateAsync(id, data, Ct))).Succeeded);
    }

    private async Task<Guid> OperationAsync(string label) =>
        (await InScopeAsync<IActivityLog, IReadOnlyList<ActivityRow>>(a => a.RecentAsync(null, 50, Ct))).Single(r => r.Label == label).Id;

    private async Task<UndoCheck> CheckAsync(string label)
    {
        var id = await OperationAsync(label);
        return await InScopeAsync<IOperationUndo, UndoCheck>(u => u.CheckAsync(id, Ct));
    }

    private async Task<Result> UndoAsync(string label)
    {
        var id = await OperationAsync(label);
        return await InScopeAsync<IOperationUndo, Result>(u => u.UndoAsync(id, Ct));
    }

    /// <summary>Runs the call inside an operation of the given label (as an MCP call or an import does).</summary>
    private async Task<TResult> InOperationAsync<TService, TResult>(string label, Func<TService, Task<TResult>> action)
        where TService : notnull
    {
        await using var scope = environment.Services.CreateAsyncScope();
        using var operation = scope.ServiceProvider.GetRequiredService<IOperationScope>().Begin(ChangeChannel.Ui, label);
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }

    private async Task<TResult> InScopeAsync<TService, TResult>(Func<TService, Task<TResult>> action)
        where TService : notnull
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }

    [Fact]
    public async Task A_lease_is_taken_back_with_its_parties()
    {
        var (_, unit) = await PropertyWithUnitAsync();
        var tenant = await CreatePersonAsync("Mieter", "mieter@example.de");
        var lease = (await InOperationAsync<ILeaseService, Result>("Vertrag", s => s.CreateAsync(
            new LeaseCreation { RentalUnitId = unit, Start = Today, TenantIds = [tenant], BaseRent = 650m, OperatingCostPrepayment = 180m, HeatingCostPrepayment = 90m, People = 1 }, Ct)));
        Assert.True(lease.Succeeded, string.Join(", ", lease.Errors));

        var check = await CheckAsync("Vertrag");

        Assert.True(check.CanUndo, check.Reason);
        Assert.True((await UndoAsync("Vertrag")).Succeeded);
        Assert.Null(await InScopeAsync<ILeaseService, LeaseDetails?>(s => s.GetAsync(lease.Id!.Value, Ct)));
    }

    [Fact]
    public async Task A_meter_is_taken_back_with_its_first_reading()
    {
        var (propertyId, _) = await PropertyWithUnitAsync();
        var meter = await InOperationAsync<IMeterService, Result>("Zähler", s => s.CreateAsync(
            propertyId, new MeterCreation { Data = new MeterInput { Type = MeterType.ColdWater, Name = "Kaltwasser", Number = "K-1" }, InstalledOn = Today, InitialReading = 10m }, Ct));
        Assert.True(meter.Succeeded, string.Join(", ", meter.Errors));

        Assert.True((await UndoAsync("Zähler")).Succeeded);

        Assert.DoesNotContain(await InScopeAsync<IMeterService, IReadOnlyList<MeterRow>>(s => s.ListAsync(propertyId, false, Ct)), m => m.Id == meter.Id);
    }

    private async Task<(Guid PropertyId, Guid UnitId)> PropertyWithUnitAsync()
    {
        var propertyId = (await InScopeAsync<IPropertyService, Result>(s => s.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct))).Id!.Value;
        var unitId = (await InScopeAsync<IPropertyService, Result>(s => s.CreateUnitAsync(propertyId, new UnitInput { Name = "Wohnung 1" }, Ct))).Id!.Value;
        return (propertyId, unitId);
    }

    [Fact]
    public async Task A_unit_with_a_later_lease_cannot_be_taken_back()
    {
        var (_, unit) = await PropertyWithUnitAsync("Einheit");
        var tenant = await CreatePersonAsync("Mieter", "mieter@example.de");
        Assert.True((await InOperationAsync<ILeaseService, Result>("Vertrag", s => s.CreateAsync(
            new LeaseCreation { RentalUnitId = unit, Start = Today, TenantIds = [tenant], BaseRent = 650m, OperatingCostPrepayment = 180m, HeatingCostPrepayment = 90m, People = 1 }, Ct))).Succeeded);

        var check = await CheckAsync("Einheit");

        Assert.False(check.CanUndo);
    }

    [Fact]
    public async Task A_unit_with_a_later_meter_cannot_be_taken_back()
    {
        var (propertyId, unit) = await PropertyWithUnitAsync("Einheit");
        Assert.True((await InOperationAsync<IMeterService, Result>("Zähler", s => s.CreateAsync(
            propertyId, new MeterCreation { Data = new MeterInput { Type = MeterType.ColdWater, Name = "Kaltwasser", Number = "K-1", AssignmentId = unit }, InstalledOn = Today, InitialReading = 10m }, Ct))).Succeeded);

        Assert.False((await CheckAsync("Einheit")).CanUndo);
    }

    [Fact]
    public async Task A_person_who_became_a_tenant_cannot_be_taken_back()
    {
        var (_, unit) = await PropertyWithUnitAsync("Einheit");
        var tenant = await CreatePersonAsync("Mieter", "mieter@example.de");
        Assert.True((await InOperationAsync<ILeaseService, Result>("Vertrag", s => s.CreateAsync(
            new LeaseCreation { RentalUnitId = unit, Start = Today, TenantIds = [tenant], BaseRent = 650m, OperatingCostPrepayment = 180m, HeatingCostPrepayment = 90m, People = 1 }, Ct))).Succeeded);

        Assert.False((await CheckAsync("Mieter")).CanUndo);
    }

    [Fact]
    public async Task Reading_a_person_inside_a_change_does_not_stop_the_undo()
    {
        var id = await CreatePersonAsync("anlegen", "anna@example.de");
        // GetAsync logs a read access (GDPR); the change below runs in one operation together with it.
        var data = (await InScopeAsync<IPersonService, PersonDetails?>(s => s.GetAsync(id, Ct)))!.Data;
        data.Email = "neu@example.de";
        Assert.True((await InOperationAsync<IPersonService, Result>("ändern", async s =>
        {
            var result = await s.UpdateAsync(id, data, Ct);
            environment.Clock.Advance(Elocaro.Infrastructure.Audit.AccessLog.TimeWindow + TimeSpan.FromMinutes(1));
            await s.GetAsync(id, Ct); // the read joins the operation that has already recorded its change
            return result;
        })).Succeeded);

        var operationId = await OperationAsync("ändern");
        var reads = await InScopeAsync<Elocaro.Infrastructure.Persistence.ElocaroDbContext, int>(db =>
            Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(db.Set<Elocaro.Infrastructure.Audit.AuditEntry>(), a => a.OperationId == operationId && a.Action == AuditAction.Read, Ct));
        Assert.True(reads >= 1, "the read access has to be part of the operation for this test to mean anything");

        var undone = await UndoAsync("ändern");

        Assert.True(undone.Succeeded, string.Join(" ", undone.Errors));
    }

    [Fact]
    public async Task Reads_alone_make_no_operation()
    {
        var id = await CreatePersonAsync("anlegen", "anna@example.de");
        await InScopeAsync<IPersonService, PersonDetails?>(s => s.GetAsync(id, Ct));
        await InScopeAsync<IPersonService, PersonDetails?>(s => s.GetAsync(id, Ct));

        var rows = await InScopeAsync<IActivityLog, IReadOnlyList<ActivityRow>>(a => a.RecentAsync(null, 50, Ct));

        Assert.Single(rows);
    }

    private async Task<(Guid PropertyId, Guid UnitId)> PropertyWithUnitAsync(string unitLabel)
    {
        var propertyId = (await InScopeAsync<IPropertyService, Result>(s => s.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct))).Id!.Value;
        var unitId = (await InOperationAsync<IPropertyService, Result>(unitLabel, s => s.CreateUnitAsync(propertyId, new UnitInput { Name = "Wohnung 1" }, Ct))).Id!.Value;
        return (propertyId, unitId);
    }
}
