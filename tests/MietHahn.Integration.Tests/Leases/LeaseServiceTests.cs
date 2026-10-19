using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Common;
using MietHahn.Application.Leases;
using MietHahn.Application.People;
using MietHahn.Application.Privacy;
using MietHahn.Application.Properties;
using MietHahn.Domain.Leases;
using MietHahn.Integration.Tests.Infrastructure;

namespace MietHahn.Integration.Tests.Leases;

public class LeaseServiceTests(PostgresFixture postgres) : IAsyncLifetime
{
    // Start time of the test environment: 1 March 2026
    private static readonly DateOnly Today = new(2026, 3, 1);

    private InfrastructureEnvironment environment = null!;
    private Guid propertyId;
    private Guid apartment1;
    private Guid sharedFlat;
    private Guid room1;
    private Guid anna;
    private Guid ben;
    private Guid guarantor;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        environment = await InfrastructureEnvironment.CreateAsync(postgres);
        await using var scope = environment.Services.CreateAsyncScope();
        var properties = scope.ServiceProvider.GetRequiredService<IPropertyService>();
        propertyId = Validate(await properties.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct));
        apartment1 = Validate(await properties.CreateUnitAsync(propertyId, new UnitInput { Name = "Wohnung 1" }, Ct));
        sharedFlat = Validate(await properties.CreateUnitAsync(propertyId, new UnitInput { Name = "Wohnung 4" }, Ct));
        room1 = Validate(await properties.CreateRoomAsync(sharedFlat, new RoomInput { Name = "Zimmer 1" }, Ct));
        Validate(await properties.CreateRoomAsync(sharedFlat, new RoomInput { Name = "Zimmer 2" }, Ct));

        var people = scope.ServiceProvider.GetRequiredService<IPersonService>();
        anna = Validate(await people.CreateAsync(new PersonInput { FirstName = "Anna", LastName = "Schmidt" }, null, Ct));
        ben = Validate(await people.CreateAsync(new PersonInput { FirstName = "Ben", LastName = "Weber" }, null, Ct));
        guarantor = Validate(await people.CreateAsync(new PersonInput { FirstName = "Hans", LastName = "Schmidt" }, null, Ct));
    }

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    [Fact]
    public async Task Create_lease_with_two_tenants_and_guarantor()
    {
        var id = await ApartmentLeaseAsync();

        var lease = (await WithAsync(v => v.GetAsync(id, Ct)))!;

        Assert.Equal("Wohnung 1", lease.RentalUnit);
        Assert.Equal("Lindenweg 3", lease.Property);
        Assert.Equal(LeaseStatus.Active, lease.Status);
        Assert.Equal(["Anna Schmidt", "Ben Weber"], lease.Parties.Where(p => p.Role == PartyRole.Tenant).Select(p => p.Name));
        Assert.Equal("Hans Schmidt", lease.Parties.Single(p => p.Role == PartyRole.Guarantor).Name);
        Assert.Equal(650m + 180m + 90m, lease.MonthlyRent);
        Assert.Equal(1950m, lease.Data.Deposit);
        Assert.Equal(new DateOnly(2026, 3, 4), lease.NextDueDate); // 3rd working day in March 2026 (1 March is a Sunday)

        var list = Assert.Single(await WithAsync(v => v.ListAsync(true, Ct)));
        Assert.Equal("Anna Schmidt, Ben Weber", list.Tenant);
    }

    [Fact]
    public async Task Overlapping_leases_for_the_same_rental_unit_are_rejected()
    {
        await ApartmentLeaseAsync();

        var second = await WithAsync(v => v.CreateAsync(Creation(apartment1, new DateOnly(2027, 1, 1), [ben]), Ct));

        Assert.False(second.Succeeded);
        Assert.Equal("Für dieses Mietobjekt gibt es in diesem Zeitraum bereits einen Vertrag.", second.Errors.Single());
    }

    [Fact]
    public async Task Shared_flat_is_only_let_by_room_room_leases_do_not_overlap()
    {
        var wholeSharedFlat = await WithAsync(v => v.CreateAsync(Creation(sharedFlat, Today, [anna]), Ct));
        Assert.False(wholeSharedFlat.Succeeded);
        Assert.Contains("nur zimmerweise", wholeSharedFlat.Errors.Single(), StringComparison.Ordinal);

        var selection = await WithAsync(v => v.RentalUnitsAsync(Ct));
        Assert.Equal(["Wohnung 1", "Wohnung 4, Zimmer 1", "Wohnung 4, Zimmer 2"], selection.Select(m => m.Name));

        var flatRate = Creation(room1, Today, [anna]);
        flatRate.BillingType = BillingType.FlatRate;
        flatRate.BaseRent = 380m;
        flatRate.OperatingCostFlatRate = 120m;
        Validate(await WithAsync(v => v.CreateAsync(flatRate, Ct)));

        // another room in the shared flat can be created despite existing room leases
        await using var scope = environment.Services.CreateAsyncScope();
        Validate(await scope.ServiceProvider.GetRequiredService<IPropertyService>().CreateRoomAsync(sharedFlat, new RoomInput { Name = "Zimmer 3" }, Ct));
    }

    [Fact]
    public async Task Flat_rate_without_amount_is_rejected()
    {
        var creation = Creation(room1, Today, [anna]);
        creation.BillingType = BillingType.FlatRate;

        var result = await WithAsync(v => v.CreateAsync(creation, Ct));

        Assert.Equal("Bei der Abrechnungsart „Pauschale“ bitte die Betriebskostenpauschale angeben.", result.Errors.Single());
    }

    [Fact]
    public async Task Rent_increase_and_occupant_count_keep_their_history()
    {
        var id = await ApartmentLeaseAsync();

        Validate(await WithAsync(v => v.SetComponentAsync(id, new ComponentInput { Kind = RentComponentType.BaseRent, From = new DateOnly(2027, 3, 1), Amount = 700m }, Ct)));
        Validate(await WithAsync(v => v.SetOccupantCountAsync(id, new OccupantCountInput { From = new DateOnly(2026, 9, 1), Count = 3 }, Ct)));

        var lease = (await WithAsync(v => v.GetAsync(id, Ct)))!;
        var baseRents = lease.Components.Where(b => b.Kind == RentComponentType.BaseRent).ToList();
        Assert.Equal([700m, 650m], baseRents.Select(k => k.Amount));
        Assert.Equal(new DateOnly(2027, 2, 28), baseRents[1].Validity.To);
        Assert.Equal(650m, lease.CurrentComponents.Single(b => b.Kind == RentComponentType.BaseRent).Amount);
        Assert.Equal([3, 2], lease.People.Select(p => p.Count));
    }

    [Fact]
    public async Task Termination_by_tenant_calculates_the_date_and_vacancy_follows()
    {
        var id = await ApartmentLeaseAsync();

        // Notice received before the 3rd working day (4 March 2026) → ends 31 May 2026
        Validate(await WithAsync(v => v.TerminateAsync(id, new TerminationInput { ReceivedOn = new DateOnly(2026, 3, 3), By = TerminatingParty.Tenant }, Ct)));

        var lease = (await WithAsync(v => v.GetAsync(id, Ct)))!;
        Assert.Equal(new DateOnly(2026, 5, 31), lease.Term.To);
        Assert.Equal(LeaseStatus.Terminated, lease.Status);

        environment.Clock.SetUtcNow(new DateTimeOffset(2026, 6, 15, 10, 0, 0, TimeSpan.Zero));
        await using var scope = environment.Services.CreateAsyncScope();
        var property = (await scope.ServiceProvider.GetRequiredService<IPropertyService>().GetAsync(propertyId, Ct))!;
        Assert.Null(property.Units.Single(e => e.Id == apartment1).RentalUnits.Single().Occupancy);
        var history = await WithAsync(v => v.HistoryAsync(apartment1, Ct));
        Assert.True(history[0].IsVacancy);
        Assert.Equal(new DateOnly(2026, 6, 1), history[0].Period.From);
        Assert.Empty(await WithAsync(v => v.ListAsync(true, Ct)));
    }

    [Fact]
    public async Task Tenant_change_move_out_with_new_address_then_move_in_after_vacancy()
    {
        var id = await ApartmentLeaseAsync();

        Validate(await WithAsync(v => v.MoveOutAsync(id, new MoveOutInput { End = new DateOnly(2026, 6, 30), NewStreet = "Neue Straße 5", NewPostalCode = "10115", NewCity = "Berlin" }, Ct)));
        Validate(await WithAsync(v => v.CreateAsync(Creation(apartment1, new DateOnly(2026, 8, 1), [guarantor]), Ct)));

        var history = await WithAsync(v => v.HistoryAsync(apartment1, Ct));
        Assert.Equal(3, history.Count);
        Assert.Equal("Hans Schmidt", history[0].Tenant);
        Assert.True(history[1].IsVacancy);
        Assert.Equal(new Domain.Common.Period(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31)), history[1].Period);
        Assert.Equal("Anna Schmidt, Ben Weber", history[2].Tenant);

        await using var scope = environment.Services.CreateAsyncScope();
        environment.Clock.SetUtcNow(new DateTimeOffset(2026, 7, 5, 10, 0, 0, TimeSpan.Zero));
        var annaRecord = (await scope.ServiceProvider.GetRequiredService<IPersonService>().GetAsync(anna, Ct))!;
        Assert.Equal("Neue Straße 5, 10115 Berlin", annaRecord.Address!.SingleLine);
    }

    [Fact]
    public async Task Leases_appear_as_person_references_and_in_the_GDPR_access_report()
    {
        var id = await ApartmentLeaseAsync();
        await using var scope = environment.Services.CreateAsyncScope();

        var people = scope.ServiceProvider.GetRequiredService<IPersonService>();
        Assert.Equal(["Mieter"], (await people.SearchAsync(new PersonQuery("Weber"), Ct)).Single().Roles);
        var reference = (await people.GetAsync(guarantor, Ct))!.References.Single();
        Assert.Equal("Bürge", reference.Role);
        Assert.True(reference.Active);
        Assert.Equal($"/verwaltung/vertraege/{id}", reference.Link);
        Assert.False((await people.DeleteAsync(ben, Ct)).Succeeded);

        var accessReport = JsonDocument.Parse((await scope.ServiceProvider.GetRequiredService<IPrivacyService>().AccessReportAsync(anna, Ct))!).RootElement;
        var lease = accessReport.GetProperty("leases")[0];
        Assert.Equal("Wohnung 1", lease.GetProperty("rentalUnit").GetString());
        Assert.Equal(650m, lease.GetProperty("components").EnumerateArray().Single(b => b.GetProperty("kind").GetString() == "BaseRent").GetProperty("amount").GetDecimal());

        Assert.False((await scope.ServiceProvider.GetRequiredService<IPropertyService>().DeleteUnitAsync(apartment1, Ct)).Succeeded);
    }

    [Fact]
    public async Task Occupancy_appears_in_the_property_overview()
    {
        await ApartmentLeaseAsync();
        await using var scope = environment.Services.CreateAsyncScope();

        var property = (await scope.ServiceProvider.GetRequiredService<IPropertyService>().GetAsync(propertyId, Ct))!;
        var occupancy = property.Units.Single(e => e.Id == apartment1).RentalUnits.Single().Occupancy!;
        Assert.Equal("Anna Schmidt, Ben Weber", occupancy.Tenant);
        Assert.Equal(Today, occupancy.Start);
        var overview = (await scope.ServiceProvider.GetRequiredService<IPropertyService>().ListAsync(null, Ct)).Single();
        Assert.Equal(1, overview.Rented);
        Assert.Equal(2, overview.Vacancy);
    }

    private async Task<Guid> ApartmentLeaseAsync()
    {
        var creation = Creation(apartment1, Today, [anna, ben]);
        creation.GuarantorIds = [guarantor];
        creation.Data.Deposit = 1950m;
        return Validate(await WithAsync(v => v.CreateAsync(creation, Ct)));
    }

    private static LeaseCreation Creation(Guid rentalUnit, DateOnly start, List<Guid> tenant) => new()
    {
        RentalUnitId = rentalUnit,
        Start = start,
        TenantIds = tenant,
        BaseRent = 650m,
        OperatingCostPrepayment = 180m,
        HeatingCostPrepayment = 90m,
        People = 2,
    };

    private static Guid Validate(Result result)
    {
        Assert.True(result.Succeeded, string.Join(", ", result.Errors));
        return result.Id!.Value;
    }

    private async Task<T> WithAsync<T>(Func<ILeaseService, Task<T>> action)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<ILeaseService>());
    }
}
