using Elocaro.Application.Accounts;
using Elocaro.Application.Common;
using Elocaro.Application.Leases;
using Elocaro.Application.People;
using Elocaro.Application.Properties;
using Elocaro.Domain.People;
using Elocaro.Integration.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Integration.Tests.People;

/// <summary>Which role <c>SetUpAccessAsync</c> gives a person: the contact type wins, then owners who are no tenants, otherwise tenant.</summary>
public class AccessRoleTests(PostgresFixture postgres) : IAsyncLifetime
{
    private InfrastructureEnvironment environment = null!;
    private Guid property;
    private Guid unit;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        environment = await InfrastructureEnvironment.CreateAsync(postgres);
        await using var scope = environment.Services.CreateAsyncScope();
        var properties = scope.ServiceProvider.GetRequiredService<IPropertyService>();
        property = Validate(await properties.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct));
        unit = Validate(await properties.CreateUnitAsync(property, new UnitInput { Name = "Wohnung 1" }, Ct));
    }

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    [Fact]
    public async Task Owner_who_is_no_tenant_gets_the_owner_role()
    {
        var person = await PersonAsync("owner");
        await OwnAsync(person);

        Assert.Equal(Roles.Owner, await AccessRoleAsync(person));
    }

    [Fact]
    public async Task Tenant_gets_the_tenant_role()
    {
        var person = await PersonAsync("tenant");
        await RentAsync(tenant: person);

        Assert.Equal(Roles.Tenant, await AccessRoleAsync(person));
    }

    [Fact]
    public async Task Owner_who_is_also_a_tenant_gets_the_tenant_role()
    {
        var person = await PersonAsync("both");
        await OwnAsync(person);
        await RentAsync(tenant: person);

        Assert.Equal(Roles.Tenant, await AccessRoleAsync(person));
    }

    [Fact]
    public async Task Owner_who_is_only_a_guarantor_gets_the_tenant_role()
    {
        var person = await PersonAsync("guarantor");
        var tenant = await PersonAsync("other");
        await OwnAsync(person);
        await RentAsync(tenant: tenant, guarantor: person);

        Assert.Equal(Roles.Tenant, await AccessRoleAsync(person));
    }

    [Fact]
    public async Task Person_without_any_relation_gets_the_tenant_role()
    {
        Assert.Equal(Roles.Tenant, await AccessRoleAsync(await PersonAsync("nobody")));
    }

    [Fact]
    public async Task Caretaker_and_tradesperson_get_their_own_roles_even_when_they_own_a_property()
    {
        var caretaker = await PersonAsync("caretaker", ContactType.Caretaker);
        var tradesperson = await PersonAsync("tradesperson", ContactType.Tradesperson);
        await OwnAsync(caretaker);
        await OwnAsync(tradesperson, share: 40m);

        Assert.Equal(Roles.Caretaker, await AccessRoleAsync(caretaker));
        Assert.Equal(Roles.Tradesperson, await AccessRoleAsync(tradesperson));
    }

    private async Task<string> AccessRoleAsync(Guid person)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        var people = scope.ServiceProvider.GetRequiredService<IPersonService>();
        Validate(await people.SetUpAccessAsync(person, Ct));
        return (await people.GetAsync(person, Ct))!.Access!.Role;
    }

    private async Task<Guid> PersonAsync(string name, ContactType type = ContactType.None)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return Validate(await scope.ServiceProvider.GetRequiredService<IPersonService>()
            .CreateAsync(new PersonInput { FirstName = name, LastName = "Test", Email = $"{name}@example.de", ContactType = type }, null, Ct));
    }

    private async Task OwnAsync(Guid person, decimal share = 50m)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        Validate(await scope.ServiceProvider.GetRequiredService<IPropertyOwners>().AddAsync(property, new OwnerInput { PersonId = person, SharePercent = share }, Ct));
    }

    private async Task RentAsync(Guid tenant, Guid? guarantor = null)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        var creation = new LeaseCreation
        {
            RentalUnitId = unit,
            Start = new DateOnly(2026, 1, 1),
            TenantIds = [tenant],
            BaseRent = 650m,
            OperatingCostPrepayment = 180m,
            HeatingCostPrepayment = 90m,
            People = 1,
        };
        if (guarantor is { } g)
        {
            creation.GuarantorIds = [g];
        }

        Validate(await scope.ServiceProvider.GetRequiredService<ILeaseService>().CreateAsync(creation, Ct));
    }

    private static Guid Validate(Result result)
    {
        Assert.True(result.Succeeded, string.Join(", ", result.Errors));
        return result.Id!.Value;
    }
}
