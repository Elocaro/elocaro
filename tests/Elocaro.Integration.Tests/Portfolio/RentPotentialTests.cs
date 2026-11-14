using System.Security.Claims;
using Elocaro.Application.Accounts;
using Elocaro.Application.Common;
using Elocaro.Application.Leases;
using Elocaro.Application.People;
using Elocaro.Application.Portfolio;
using Elocaro.Application.Properties;
using Elocaro.Domain.Portfolio;
using Elocaro.Integration.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Integration.Tests.Portfolio;

public class RentPotentialTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly ClaimsPrincipal Admin = new(new ClaimsIdentity([new Claim(ClaimTypes.Role, Roles.Admin)], "test"));

    private InfrastructureEnvironment environment = null!;
    private Guid propertyId;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        environment = await InfrastructureEnvironment.CreateAsync(postgres);
        environment.Clock.SetUtcNow(new DateTimeOffset(2027, 2, 10, 10, 0, 0, TimeSpan.Zero));
        await using var scope = environment.Services.CreateAsyncScope();
        var properties = scope.ServiceProvider.GetRequiredService<IPropertyService>();
        propertyId = Validate(await properties.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct));
        var apartment = Validate(await properties.CreateUnitAsync(propertyId, new UnitInput { Name = "Wohnung 1" }, Ct));
        Validate(await properties.SetUnitAreaAsync(apartment, new AreaInput { From = new DateOnly(2019, 1, 1), Area = 60m }, Ct));
        var anna = Validate(await scope.ServiceProvider.GetRequiredService<IPersonService>().CreateAsync(new PersonInput { FirstName = "Anna", LastName = "Schmidt" }, null, Ct));
        Validate(await scope.ServiceProvider.GetRequiredService<ILeaseService>().CreateAsync(new LeaseCreation
        {
            RentalUnitId = apartment,
            Start = new DateOnly(2020, 1, 1),
            TenantIds = [anna],
            BaseRent = 600m,
            OperatingCostPrepayment = 150m,
        }, Ct));
    }

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    [Fact]
    public async Task Without_a_rent_index_value_there_is_nothing_to_compare()
    {
        await using var scope = environment.Services.CreateAsyncScope();

        var potential = (await scope.ServiceProvider.GetRequiredService<IRentPotential>().GetAsync(Admin, propertyId, Ct))!;

        Assert.Null(potential.PerSquareMetre);
        Assert.Empty(potential.Rows);
    }

    [Fact]
    public async Task The_rent_index_value_gives_the_potential_within_the_cap_limit()
    {
        await using var scope = environment.Services.CreateAsyncScope();
        var properties = scope.ServiceProvider.GetRequiredService<IPropertyService>();
        var data = (await properties.GetAsync(propertyId, Ct))!.Data;
        data.RentIndexPerSquareMetre = 14m;
        Validate(await properties.UpdateAsync(propertyId, data, Ct));

        var potential = (await scope.ServiceProvider.GetRequiredService<IRentPotential>().GetAsync(Admin, propertyId, Ct))!;

        var row = Assert.Single(potential.Rows);
        Assert.Equal(840m, row.ComparativeRent); // 60 m² × 14 €
        Assert.Equal(720m, row.CapLimit); // 600 € + 20 %
        Assert.Equal(120m, row.Potential);
        Assert.Equal(RentPotentialLimit.CapLimit, row.Limit);
        Assert.Equal(120m, potential.TotalPotential);
    }

    [Fact]
    public async Task Without_finance_rights_or_for_an_unknown_property_there_is_no_potential()
    {
        await using var scope = environment.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IRentPotential>();

        Assert.Null(await service.GetAsync(new ClaimsPrincipal(new ClaimsIdentity([], "test")), propertyId, Ct));
        Assert.Null(await service.GetAsync(Admin, Guid.NewGuid(), Ct));
    }

    private static Guid Validate(Result result)
    {
        Assert.True(result.Succeeded, string.Join(", ", result.Errors));
        return result.Id!.Value;
    }
}
