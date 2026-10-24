using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Accounts;
using MietHahn.Application.Admin;
using MietHahn.Application.Bank;
using MietHahn.Application.Common;
using MietHahn.Application.Leases;
using MietHahn.Application.People;
using MietHahn.Application.Properties;
using MietHahn.Application.Todos;
using MietHahn.Integration.Tests.Infrastructure;

namespace MietHahn.Integration.Tests.Admin;

public class SetupChecklistTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly ClaimsPrincipal Administrator = new(new ClaimsIdentity([new Claim(ClaimTypes.Role, Roles.Admin)], "test"));

    private InfrastructureEnvironment environment = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => environment = await InfrastructureEnvironment.CreateAsync(postgres);

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    [Fact]
    public async Task A_fresh_installation_lists_the_first_steps_with_the_first_one_due_soon()
    {
        var todos = await ChecklistAsync(Administrator);

        Assert.Equal(
            ["Vermieterprofil anlegen", "Erstes Objekt anlegen", "Erste Einheit anlegen", "Wohnfläche eintragen", "Ersten Mieter anlegen", "Ersten Mietvertrag anlegen", "Bankkonto anlegen"],
            todos.Select(t => t.Title));
        Assert.Equal(Urgency.Soon, todos[0].Urgency);
        Assert.All(todos.Skip(1), t => Assert.Equal(Urgency.Info, t.Urgency));
        Assert.All(todos, t => Assert.Equal("So geht es los", t.Area));
        Assert.Contains("0 von 7", todos[0].Details, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Steps_disappear_as_they_are_done_and_the_whole_list_is_gone_when_everything_exists()
    {
        await using var scope = environment.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        await sp.GetRequiredService<ILandlordProfileService>().SaveAsync(
            new LandlordProfileInput { Name = "Hausverwaltung Beispiel", Street = "Weg 1", PostalCode = "50667", City = "Köln" }, Ct);
        var properties = sp.GetRequiredService<IPropertyService>();
        var house = Validate(await properties.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct));
        var unit = Validate(await properties.CreateUnitAsync(house, new UnitInput { Name = "Wohnung 1" }, Ct));

        var todos = await ChecklistAsync(Administrator);
        Assert.Equal(["Wohnfläche eintragen", "Ersten Mieter anlegen", "Ersten Mietvertrag anlegen", "Bankkonto anlegen"], todos.Select(t => t.Title));
        Assert.Equal($"/verwaltung/einheiten/{unit}", todos[0].Link);
        Assert.Contains("3 von 7", todos[0].Details, StringComparison.Ordinal);

        Validate(await properties.SetUnitAreaAsync(unit, new AreaInput { From = new DateOnly(2020, 1, 1), Area = 55m }, Ct));
        var person = Validate(await sp.GetRequiredService<IPersonService>().CreateAsync(new PersonInput { LastName = "Schmidt" }, null, Ct));
        Validate(await sp.GetRequiredService<ILeaseService>().CreateAsync(
            new LeaseCreation { RentalUnitId = unit, Start = new DateOnly(2026, 1, 1), TenantIds = [person], BaseRent = 650m }, Ct));
        Assert.Equal(["Bankkonto anlegen"], (await ChecklistAsync(Administrator)).Select(t => t.Title));

        Validate(await sp.GetRequiredService<IBankService>().CreateAccountAsync(new BankAccountInput { Name = "Mietkonto", Iban = "DE89370400440532013000" }, Ct));
        Assert.Empty(await ChecklistAsync(Administrator));
    }

    [Fact]
    public async Task Steps_the_user_may_not_do_are_left_out()
    {
        var manager = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, Roles.Manager), new Claim(Permissions.ClaimType, nameof(AccessRight.MasterData))], "test"));

        var todos = await ChecklistAsync(manager);

        Assert.Equal(["Erstes Objekt anlegen", "Erste Einheit anlegen", "Wohnfläche eintragen", "Ersten Mieter anlegen", "Ersten Mietvertrag anlegen"], todos.Select(t => t.Title));
        Assert.Contains("0 von 5", todos[0].Details, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Users_without_master_data_rights_do_not_see_the_checklist()
    {
        Assert.Empty(await ChecklistAsync(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, Roles.Tenant)], "test"))));
    }

    private async Task<IReadOnlyList<TodoItem>> ChecklistAsync(ClaimsPrincipal user)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        var source = scope.ServiceProvider.GetServices<ITodoSource>().Single(s => s.GetType().Name == "SetupChecklistTodos");
        return await source.TodosAsync(user, Ct);
    }

    private static Guid Validate(Result result)
    {
        Assert.True(result.Succeeded, string.Join(", ", result.Errors));
        return result.Id!.Value;
    }
}
