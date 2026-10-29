using Elocaro.Application.Accounts;
using Elocaro.Application.Common;
using Elocaro.Application.Leases;
using Elocaro.Application.Metering;
using Elocaro.Application.People;
using Elocaro.Application.Properties;
using Elocaro.Domain.Communication;
using Elocaro.Domain.Metering;
using Elocaro.Domain.People;
using Elocaro.Infrastructure.Identity;
using Elocaro.Infrastructure.Metering;
using Elocaro.Infrastructure.Persistence;
using Elocaro.Integration.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Integration.Tests.Metering;

public class ConsumptionInformationTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateOnly March = new(2026, 3, 1);

    private InfrastructureEnvironment environment = null!;
    private Guid property;
    private readonly List<(Guid Lease, Guid Person)> tenants = [];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        environment = await InfrastructureEnvironment.CreateAsync(postgres);
        environment.Clock.SetUtcNow(new DateTimeOffset(2026, 4, 2, 8, 0, 0, TimeSpan.Zero));
        await using var scope = environment.Services.CreateAsyncScope();
        property = Validate(await scope.ServiceProvider.GetRequiredService<IPropertyService>().CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct));
    }

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    /// <summary>An apartment with area, tenant and heat meter; the readings give <paramref name="march"/> kWh in March 2026, 200 in February and 100 in March 2025.</summary>
    private async Task<Guid> ApartmentAsync(int number, decimal area, decimal march)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        var properties = scope.ServiceProvider.GetRequiredService<IPropertyService>();
        var unit = Validate(await properties.CreateUnitAsync(property, new UnitInput { Name = $"Wohnung {number}" }, Ct));
        Validate(await properties.SetUnitAreaAsync(unit, new AreaInput { From = new DateOnly(2024, 1, 1), Area = area }, Ct));
        var person = Validate(await scope.ServiceProvider.GetRequiredService<IPersonService>().CreateAsync(new PersonInput { FirstName = $"Mieter{number}", LastName = "Test", Email = $"mieter{number}@example.de" }, null, Ct));
        var lease = Validate(await scope.ServiceProvider.GetRequiredService<ILeaseService>().CreateAsync(new LeaseCreation { RentalUnitId = unit, Start = new DateOnly(2024, 1, 1), TenantIds = [person], BaseRent = 500m, People = 1 }, Ct));
        tenants.Add((lease, person));

        var meters = scope.ServiceProvider.GetRequiredService<IMeterService>();
        var meter = Validate(await meters.CreateAsync(property, new MeterCreation
        {
            Data = new MeterInput { Type = MeterType.Heat, Name = "Wärme", Number = $"WMZ-{number}", AssignmentId = unit },
            InstalledOn = new DateOnly(2024, 12, 31),
            InitialReading = 0m,
        }, Ct));
        foreach (var (date, value) in new[]
        {
            (new DateOnly(2025, 2, 28), 100m), (new DateOnly(2025, 3, 31), 200m),
            (new DateOnly(2026, 1, 31), 1000m), (new DateOnly(2026, 2, 28), 1200m), (new DateOnly(2026, 3, 31), 1200m + march),
        })
        {
            var result = await meters.RecordReadingAsync(meter, new ReadingInput { Date = date, Value = value, Confirmed = true }, null, Ct);
            Assert.True(result.Succeeded, string.Join(", ", result.Errors));
        }

        return lease;
    }

    [Fact]
    public async Task March_is_compared_with_february_march_of_last_year_and_the_average_of_three_apartments()
    {
        var first = await ApartmentAsync(1, 50m, 300m);
        await ApartmentAsync(2, 60m, 360m);
        await ApartmentAsync(3, 70m, 280m);

        var month = (await WithAsync(i => i.ForLeaseMonthAsync(first, March, Ct)))!;

        var line = Assert.Single(month.Lines);
        Assert.Equal((300m, 200m, 100m), (line.Current, line.Previous, line.LastYear));
        Assert.Equal(6m, line.Own);
        Assert.Equal((5.333m, 3), (line.Average, line.Comparisons));
        Assert.Equal("je m²", line.BasisText);
        Assert.Equal(MeasurementUnit.KilowattHours, line.Unit);
    }

    [Fact]
    public async Task With_fewer_than_three_apartments_there_is_no_average_and_missing_readings_leave_the_month_open()
    {
        var first = await ApartmentAsync(1, 50m, 300m);
        await ApartmentAsync(2, 60m, 360m);

        var line = Assert.Single((await WithAsync(i => i.ForLeaseMonthAsync(first, March, Ct)))!.Lines);
        Assert.Null(line.Average);
        Assert.Equal(2, line.Comparisons);

        // After the last reading nothing is extrapolated.
        var april = Assert.Single((await WithAsync(i => i.ForLeaseMonthAsync(first, new DateOnly(2026, 4, 1), Ct)))!.Lines);
        Assert.Null(april.Current);
        Assert.NotNull(april.Hint);
        Assert.Equal(300m, april.Previous);
    }

    [Fact]
    public async Task A_tenant_sees_only_the_own_apartment_and_other_accounts_see_nothing()
    {
        var first = await ApartmentAsync(1, 50m, 300m);
        await ApartmentAsync(2, 60m, 360m);
        await ApartmentAsync(3, 70m, 280m);
        var anna = await LinkedAccountAsync(tenants[0].Person, Roles.Tenant);
        var stranger = await LinkedAccountAsync(null, Roles.Tenant);

        var own = Assert.Single(await WithAsync(i => i.ForTenantAsync(anna, 12, Ct)));

        Assert.Equal(first, own.LeaseId);
        Assert.Equal(March, own.Months[0].Month);
        Assert.Empty(await WithAsync(i => i.ForTenantAsync(stranger, 12, Ct)));
    }

    [Fact]
    public async Task The_monthly_mail_goes_out_once_after_the_third_and_only_when_the_readings_are_in()
    {
        await ApartmentAsync(1, 50m, 300m);
        await ApartmentAsync(2, 60m, 360m);
        await ApartmentAsync(3, 70m, 280m);
        await LinkedAccountAsync(tenants[0].Person, Roles.Tenant);

        Assert.Equal(0, await SendAsync());

        environment.Clock.SetUtcNow(new DateTimeOffset(2026, 4, 10, 8, 0, 0, TimeSpan.Zero));
        Assert.Equal(1, await SendAsync());
        Assert.Equal(0, await SendAsync());

        var mail = Assert.Single(await WithDbAsync(db => db.Set<OutgoingMail>().Where(m => m.Template == "consumption-information").ToListAsync(Ct)));
        Assert.Equal("mieter1@example.de", mail.To.Single());
        Assert.Contains("März 2026", mail.Html, StringComparison.Ordinal);
        Assert.Contains("300 kWh", mail.Html, StringComparison.Ordinal);
        Assert.Contains("Vormonat: 200 kWh (+50 %)", mail.Html, StringComparison.Ordinal);
        Assert.Contains("Durchschnitt der Wohnungen im Haus: 5,33 kWh je m²", mail.Html, StringComparison.Ordinal);
        Assert.Equal(3, (await WithDbAsync(db => db.Set<ConsumptionInformationDispatch>().ToListAsync(Ct))).Count);
    }

    [Fact]
    public async Task Without_readings_nothing_is_sent_and_the_next_day_tries_again()
    {
        var first = await ApartmentAsync(1, 50m, 300m);
        await LinkedAccountAsync(tenants[0].Person, Roles.Tenant);
        await using (var scope = environment.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ElocaroDbContext>();
            var meter = await db.Set<Elocaro.Domain.Metering.Meter>().Include(z => z.Readings).SingleAsync(Ct);
            meter.RemoveReading(meter.Readings.Single(r => r.Date == new DateOnly(2026, 3, 31)).Id);
            await db.SaveChangesAsync(Ct);
        }

        Assert.Equal(0, await SendAsync());
        Assert.Empty(await WithDbAsync(db => db.Set<ConsumptionInformationDispatch>().Where(d => d.LeaseId == first).ToListAsync(Ct)));
    }

    private async Task<int> SendAsync()
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ConsumptionNotifications>().SendAsync(Ct);
    }

    private async Task<T> WithAsync<T>(Func<IConsumptionInformation, Task<T>> action)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IConsumptionInformation>());
    }

    private async Task<T> WithDbAsync<T>(Func<ElocaroDbContext, Task<T>> action)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<ElocaroDbContext>());
    }

    private async Task<System.Security.Claims.ClaimsPrincipal> LinkedAccountAsync(Guid? personId, string role)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = $"{Guid.NewGuid():N}@example.de", Email = $"{Guid.NewGuid():N}@example.de", DisplayName = "Konto" };
        Assert.True((await manager.CreateAsync(user)).Succeeded);
        Assert.True((await manager.AddToRoleAsync(user, role)).Succeeded);
        if (personId is { } id)
        {
            var db = scope.ServiceProvider.GetRequiredService<ElocaroDbContext>();
            (await db.Set<Person>().SingleAsync(p => p.Id == id, Ct)).UserId = user.Id;
            await db.SaveChangesAsync(Ct);
        }

        return new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, user.Id.ToString()), new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, role)], "test"));
    }

    private static Guid Validate(Result result)
    {
        Assert.True(result.Succeeded, string.Join(", ", result.Errors));
        return result.Id!.Value;
    }
}
