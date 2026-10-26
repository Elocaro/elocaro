using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Common;
using MietHahn.Application.Leases;
using MietHahn.Application.People;
using MietHahn.Application.Properties;
using MietHahn.Domain.Common;
using MietHahn.Integration.Tests.Infrastructure;

namespace MietHahn.Integration.Tests.Properties;

public class OccupancyPlanTests(PostgresFixture postgres) : IAsyncLifetime
{
    private InfrastructureEnvironment environment = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => environment = await InfrastructureEnvironment.CreateAsync(postgres);

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    [Fact]
    public async Task The_plan_lists_leases_per_flat_and_room_clipped_to_the_year_and_leaves_vacancy_as_gaps()
    {
        var house = Validate(await WithAsync<IPropertyService, Result>(s => s.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct)));
        var flat = Validate(await WithAsync<IPropertyService, Result>(s => s.CreateUnitAsync(house, new UnitInput { Name = "Wohnung 1" }, Ct)));
        var wg = Validate(await WithAsync<IPropertyService, Result>(s => s.CreateUnitAsync(house, new UnitInput { Name = "WG" }, Ct)));
        var room1 = Validate(await WithAsync<IPropertyService, Result>(s => s.CreateRoomAsync(wg, new RoomInput { Name = "Zimmer 1" }, Ct)));
        Validate(await WithAsync<IPropertyService, Result>(s => s.CreateRoomAsync(wg, new RoomInput { Name = "Zimmer 2" }, Ct)));
        var anna = Validate(await WithAsync<IPersonService, Result>(s => s.CreateAsync(new PersonInput { FirstName = "Anna", LastName = "Schmidt" }, null, Ct)));
        var ben = Validate(await WithAsync<IPersonService, Result>(s => s.CreateAsync(new PersonInput { FirstName = "Ben", LastName = "Meier" }, null, Ct)));
        Validate(await WithAsync<ILeaseService, Result>(s => s.CreateAsync(new LeaseCreation { RentalUnitId = flat, Start = new DateOnly(2024, 6, 1), TenantIds = [anna], BaseRent = 500m, OperatingCostPrepayment = 100m }, Ct)));
        Validate(await WithAsync<ILeaseService, Result>(s => s.CreateAsync(new LeaseCreation { RentalUnitId = room1, Start = new DateOnly(2026, 3, 1), FixedTermUntil = new DateOnly(2026, 8, 31), TenantIds = [ben], BaseRent = 300m }, Ct)));

        var plan = (await WithAsync<IOccupancyPlan, OccupancyPlan?>(s => s.GetAsync(house, Period.CalendarYear(2026), Ct)))!;

        Assert.Equal(["Wohnung 1", "Zimmer 1", "Zimmer 2"], plan.Rows.Select(r => r.Label));
        Assert.Equal([null, "WG", "WG"], plan.Rows.Select(r => r.Group));
        var annaBar = Assert.Single(plan.Rows[0].Bars);
        Assert.Equal((new Period(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)), new DateOnly(2024, 6, 1), true, 600m), (annaBar.Period, annaBar.LeaseStart, annaBar.Open, annaBar.Rent));
        var benBar = Assert.Single(plan.Rows[1].Bars);
        Assert.Equal((new Period(new DateOnly(2026, 3, 1), new DateOnly(2026, 8, 31)), false, "Ben Meier"), (benBar.Period, benBar.Open, benBar.Tenant));
        Assert.Empty(plan.Rows[2].Bars);
        Assert.Null(await WithAsync<IOccupancyPlan, OccupancyPlan?>(s => s.GetAsync(Guid.NewGuid(), Period.CalendarYear(2026), Ct)));
    }

    [Fact]
    public async Task One_lease_can_cover_several_rooms_and_a_room_cannot_be_rented_twice()
    {
        var house = Validate(await WithAsync<IPropertyService, Result>(s => s.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct)));
        var wg = Validate(await WithAsync<IPropertyService, Result>(s => s.CreateUnitAsync(house, new UnitInput { Name = "WG" }, Ct)));
        var room1 = Validate(await WithAsync<IPropertyService, Result>(s => s.CreateRoomAsync(wg, new RoomInput { Name = "Zimmer 1" }, Ct)));
        var room2 = Validate(await WithAsync<IPropertyService, Result>(s => s.CreateRoomAsync(wg, new RoomInput { Name = "Zimmer 2" }, Ct)));
        var room3 = Validate(await WithAsync<IPropertyService, Result>(s => s.CreateRoomAsync(wg, new RoomInput { Name = "Zimmer 3" }, Ct)));
        var anna = Validate(await WithAsync<IPersonService, Result>(s => s.CreateAsync(new PersonInput { FirstName = "Anna", LastName = "Schmidt" }, null, Ct)));
        var ben = Validate(await WithAsync<IPersonService, Result>(s => s.CreateAsync(new PersonInput { FirstName = "Ben", LastName = "Meier" }, null, Ct)));
        var lease = Validate(await WithAsync<ILeaseService, Result>(s => s.CreateAsync(new LeaseCreation { RentalUnitId = room1, AdditionalRoomIds = [room2], Start = new DateOnly(2026, 1, 1), TenantIds = [anna], BaseRent = 500m }, Ct)));

        var other = await WithAsync<ILeaseService, Result>(s => s.CreateAsync(new LeaseCreation { RentalUnitId = room2, Start = new DateOnly(2026, 6, 1), TenantIds = [ben], BaseRent = 300m }, Ct));
        Assert.False(other.Succeeded);
        Validate(await WithAsync<ILeaseService, Result>(s => s.CreateAsync(new LeaseCreation { RentalUnitId = room3, Start = new DateOnly(2026, 6, 1), TenantIds = [ben], BaseRent = 300m }, Ct)));

        var plan = (await WithAsync<IOccupancyPlan, OccupancyPlan?>(s => s.GetAsync(house, Period.CalendarYear(2026), Ct)))!;
        Assert.Equal(["Anna Schmidt", "Anna Schmidt", "Ben Meier"], plan.Rows.Select(r => Assert.Single(r.Bars).Tenant));

        Validate(await WithAsync<ILeaseService, Result>(s => s.RemoveRoomAsync(lease, room2, Ct)));
        var details = (await WithAsync<ILeaseService, LeaseDetails?>(s => s.GetAsync(lease, Ct)))!;
        Assert.Equal(["Zimmer 1"], details.Rooms!.Select(r => r.Name));
    }

    private static Guid Validate(Result result)
    {
        Assert.True(result.Succeeded, string.Join(", ", result.Errors));
        return result.Id!.Value;
    }

    private async Task<T> WithAsync<TService, T>(Func<TService, Task<T>> action)
        where TService : notnull
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }
}
