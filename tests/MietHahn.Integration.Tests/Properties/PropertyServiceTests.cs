using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Accounts;
using MietHahn.Application.Common;
using MietHahn.Application.Properties;
using MietHahn.Domain.Properties;
using MietHahn.Infrastructure.Audit;
using MietHahn.Infrastructure.Identity;
using MietHahn.Infrastructure.Persistence;
using MietHahn.Integration.Tests.Infrastructure;

namespace MietHahn.Integration.Tests.Properties;

public class PropertyServiceTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(InfrastructureEnvironment.StartTime.UtcDateTime);

    private InfrastructureEnvironment environment = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => environment = await InfrastructureEnvironment.CreateAsync(postgres);

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    [Fact]
    public async Task Apartment_building_with_four_flats_and_shared_flat_can_be_created()
    {
        var propertyId = await SampleHouseAsync();

        var details = (await WithAsync(v => v.GetAsync(propertyId, Ct)))!;

        Assert.Equal("Lindenweg 3, 50667 Köln", details.Address);
        Assert.Equal(4, details.Units.Count);
        var sharedFlat = details.Units.Single(e => e.IsSharedFlat);
        Assert.Equal(["Zimmer 1", "Zimmer 2", "Zimmer 3", "Zimmer 4"], sharedFlat.RentalUnits.Select(m => m.Name));
        Assert.All(sharedFlat.RentalUnits, m => Assert.True(m.IsRoom));
        Assert.Equal(400m, details.Area!.Area);
        Assert.Empty(details.ValidationHints);

        var overview = Assert.Single(await WithAsync(v => v.ListAsync(null, Ct)));
        Assert.Equal(4, overview.Units);
        Assert.Equal(7, overview.RentalUnits); // 3 apartments + 4 rooms
        Assert.Equal(7, overview.Vacancy);
        Assert.Equal(75.5m + 68m + 92m + 110m, overview.LivingArea);
    }

    [Fact]
    public async Task Self_paid_cost_types_of_a_unit_are_saved_and_only_allocable_non_heating_types_are_kept()
    {
        var propertyId = await SampleHouseAsync();
        var unit = (await WithAsync(v => v.GetAsync(propertyId, Ct)))!.Units[0];

        var input = (await WithAsync(v => v.GetUnitAsync(unit.Id, Ct)))!.Data;
        input.SelfPaidCostTypes = ["betrkv-08", "betrkv-08", "betrkv-04", "verwaltung", "unbekannt"];
        Validate(await WithAsync(v => v.UpdateUnitAsync(unit.Id, input, Ct)));

        var saved = (await WithAsync(v => v.GetUnitAsync(unit.Id, Ct)))!.Data;

        Assert.Equal(["betrkv-08"], saved.SelfPaidCostTypes);
    }

    [Fact]
    public async Task Common_area_of_a_shared_flat_is_distributed_equally_or_by_room_area()
    {
        var propertyId = await SampleHouseAsync();
        var flat = Validate(await WithAsync(v => v.CreateUnitAsync(propertyId, new UnitInput { Name = "WG" }, Ct)));
        Validate(await WithAsync(v => v.SetUnitAreaAsync(flat, new AreaInput { From = Today, Area = 100m }, Ct)));
        foreach (var (name, area) in new[] { ("Zimmer 1", 10m), ("Zimmer 2", 20m) })
        {
            var room = Validate(await WithAsync(v => v.CreateRoomAsync(flat, new RoomInput { Name = name }, Ct)));
            Validate(await WithAsync(v => v.SetRoomAreaAsync(room, new AreaInput { From = Today, Area = area }, Ct)));
        }

        Validate(await WithAsync(v => v.ApplyCommonAreaAsync(flat, CommonAreaMode.ByRoomArea, Today, Ct)));
        var shares = (await WithAsync(v => v.GetUnitAsync(flat, Ct)))!.Room.Select(r => r.Area!.SecondaryArea).ToList();
        Assert.Equal([23.33m, 46.67m], shares);

        Validate(await WithAsync(v => v.ApplyCommonAreaAsync(flat, CommonAreaMode.Equal, Today, Ct)));
        Assert.Equal([35m, 35m], (await WithAsync(v => v.GetUnitAsync(flat, Ct)))!.Room.Select(r => r.Area!.SecondaryArea));

        // Rooms bigger than the flat: nothing is changed.
        Validate(await WithAsync(v => v.SetUnitAreaAsync(flat, new AreaInput { From = Today, Area = 25m }, Ct)));
        Assert.False((await WithAsync(v => v.ApplyCommonAreaAsync(flat, CommonAreaMode.Equal, Today, Ct))).Succeeded);
    }

    [Fact]
    public async Task Area_change_continues_series_and_keeps_history()
    {
        var propertyId = await SampleHouseAsync();
        var unitId = (await WithAsync(v => v.GetAsync(propertyId, Ct)))!.Units[0].Id;

        Validate(await WithAsync(v => v.SetUnitAreaAsync(unitId, new AreaInput { From = Today.AddDays(10), Area = 80m, SecondaryArea = 78m }, Ct)));

        var unit = (await WithAsync(v => v.GetUnitAsync(unitId, Ct)))!;
        Assert.Equal(75.5m, unit.Area!.Area); // still the old area today
        Assert.Equal(2, unit.AreaHistory.Count);
        Assert.Equal(new(80m, 78m), (unit.AreaHistory[0].Area, unit.AreaHistory[0].SecondaryArea));
        Assert.Equal(Today.AddDays(9), unit.AreaHistory[1].Validity.To);

        var retroactive = await WithAsync(v => v.SetUnitAreaAsync(unitId, new AreaInput { From = Today.AddDays(-30), Area = 70m }, Ct));
        Assert.False(retroactive.Succeeded);
        Assert.Contains("Stichtag", retroactive.Errors[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Changes_appear_in_the_audit_log()
    {
        var propertyId = await SampleHouseAsync();
        var data = (await WithAsync(v => v.GetAsync(propertyId, Ct)))!.Data;
        data.YearBuilt = 1972;

        Validate(await WithAsync(v => v.UpdateAsync(propertyId, data, Ct)));

        await using var scope = environment.Services.CreateAsyncScope();
        var entries = await scope.ServiceProvider.GetRequiredService<MietHahnDbContext>().Set<AuditEntry>()
            .Where(a => a.EntityType == nameof(Property) && a.EntityId == propertyId).ToListAsync(Ct);
        Assert.Contains(entries, e => e.ChangesJson.Contains("1972", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Duplicate_unit_is_rejected_with_message()
    {
        var propertyId = await SampleHouseAsync();

        var result = await WithAsync(v => v.CreateUnitAsync(propertyId, new UnitInput { Name = "Wohnung 2" }, Ct));

        Assert.False(result.Succeeded);
        Assert.Equal("Im Objekt gibt es bereits eine Einheit „Wohnung 2“.", result.Errors.Single());
    }

    [Fact]
    public async Task Delete_only_from_bottom_to_top()
    {
        var propertyId = await SampleHouseAsync();
        var sharedFlat = (await WithAsync(v => v.GetAsync(propertyId, Ct)))!.Units.Single(e => e.IsSharedFlat);

        Assert.False((await WithAsync(v => v.DeleteAsync(propertyId, Ct))).Succeeded);
        Assert.False((await WithAsync(v => v.DeleteUnitAsync(sharedFlat.Id, Ct))).Succeeded);

        foreach (var room in sharedFlat.RentalUnits)
        {
            Validate(await WithAsync(v => v.DeleteRoomAsync(room.Id, Ct)));
        }

        Validate(await WithAsync(v => v.DeleteUnitAsync(sharedFlat.Id, Ct)));
        var afterwards = (await WithAsync(v => v.GetAsync(propertyId, Ct)))!;
        Assert.Equal(3, afterwards.Units.Count);

        // Soft-deleted: data is retained for history/audit.
        await using var scope = environment.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MietHahnDbContext>();
        Assert.True(await db.Set<Room>().IgnoreQueryFilters().CountAsync(z => z.UnitId == sharedFlat.Id && z.Deleted, Ct) == 4);
    }

    [Fact]
    public async Task Caretaker_gets_only_assigned_properties()
    {
        var houseA = await SampleHouseAsync();
        var houseB = Validate(await WithAsync(v => v.CreateAsync(new PropertyInput { Name = "Birkenallee 9", Street = "Birkenallee 9", PostalCode = "50668", City = "Köln" }, Ct)));
        var caretaker = await AccountAsync("hm@example.de", Roles.Caretaker);
        var tenant = await AccountAsync("m@example.de", Roles.Tenant);

        await using (var scope = environment.Services.CreateAsyncScope())
        {
            var assignments = scope.ServiceProvider.GetRequiredService<IPropertyAssignments>();
            Validate(await assignments.SetAsync(caretaker, [houseB], Ct));
            Assert.False((await assignments.SetAsync(tenant, [houseA], Ct)).Succeeded);
            Assert.False((await assignments.SetAsync(caretaker, [Guid.CreateVersion7()], Ct)).Succeeded);

            Assert.Equal([houseB], await assignments.PropertiesOfAsync(caretaker, Ct));
            Assert.True(await assignments.IsAssignedAsync(caretaker, houseB, Ct));
            Assert.False(await assignments.IsAssignedAsync(caretaker, houseA, Ct));

            // Replace and add again (unique index ignores deleted assignments)
            Validate(await assignments.SetAsync(caretaker, [houseA], Ct));
            Validate(await assignments.SetAsync(caretaker, [houseA, houseB], Ct));
            Assert.Equal(2, (await assignments.PropertiesOfAsync(caretaker, Ct)).Count);
        }

        var visible = await WithAsync(v => v.ListAsync([houseB], Ct));
        Assert.Equal("Birkenallee 9", Assert.Single(visible).Name);
    }

    private async Task<Guid> SampleHouseAsync()
    {
        var propertyId = Validate(await WithAsync(v => v.CreateAsync(new PropertyInput
        {
            Name = "Lindenweg 3",
            Street = "Lindenweg 3",
            PostalCode = "50667",
            City = "Köln",
            YearBuilt = 1965,
            HeatingType = HeatingType.CentralHeating,
            EnergySource = EnergySource.NaturalGas,
        }, Ct)));
        Validate(await WithAsync(v => v.SetAreaAsync(propertyId, new AreaInput { From = Today, Area = 400m, SecondaryArea = 30m }, Ct)));

        var areas = new[] { 75.5m, 68m, 92m, 110m };
        for (var i = 0; i < 4; i++)
        {
            var unit = Validate(await WithAsync(v => v.CreateUnitAsync(propertyId, new UnitInput { Name = $"Wohnung {i + 1}", Location = i < 2 ? "EG" : "OG" }, Ct)));
            Validate(await WithAsync(v => v.SetUnitAreaAsync(unit, new AreaInput { From = Today, Area = areas[i] }, Ct)));
            if (i == 3)
            {
                for (var z = 1; z <= 4; z++)
                {
                    var room = Validate(await WithAsync(v => v.CreateRoomAsync(unit, new RoomInput { Name = $"Zimmer {z}", Furnishing = Furnishing.Furnished }, Ct)));
                    Validate(await WithAsync(v => v.SetRoomAreaAsync(room, new AreaInput { From = Today, Area = 14m, SecondaryArea = 8m }, Ct)));
                }
            }
        }

        return propertyId;
    }

    private async Task<Guid> AccountAsync(string email, string role)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = email, Email = email, DisplayName = email };
        Assert.True((await manager.CreateAsync(user)).Succeeded);
        Assert.True((await manager.AddToRoleAsync(user, role)).Succeeded);
        return user.Id;
    }

    private static Guid Validate(Result result)
    {
        Assert.True(result.Succeeded, string.Join(", ", result.Errors));
        return result.Id!.Value;
    }

    private async Task<T> WithAsync<T>(Func<IPropertyService, Task<T>> action)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IPropertyService>());
    }
}
