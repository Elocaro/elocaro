using System.Security.Claims;
using Elocaro.Application.Accounts;
using Elocaro.Application.Common;
using Elocaro.Application.Leases;
using Elocaro.Application.Metering;
using Elocaro.Application.People;
using Elocaro.Application.Portal;
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

public class ReadingReportTests(PostgresFixture postgres) : IAsyncLifetime
{
    private InfrastructureEnvironment environment = null!;
    private Guid property;
    private Guid meter;
    private ClaimsPrincipal tenant = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        environment = await InfrastructureEnvironment.CreateAsync(postgres);
        environment.Clock.SetUtcNow(new DateTimeOffset(2026, 12, 16, 8, 0, 0, TimeSpan.Zero));
        await using var scope = environment.Services.CreateAsyncScope();
        var properties = scope.ServiceProvider.GetRequiredService<IPropertyService>();
        property = Validate(await properties.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct));
        var unit = Validate(await properties.CreateUnitAsync(property, new UnitInput { Name = "Wohnung 1" }, Ct));
        var person = Validate(await scope.ServiceProvider.GetRequiredService<IPersonService>().CreateAsync(new PersonInput { FirstName = "Anna", LastName = "Test", Email = "anna@example.de" }, null, Ct));
        Validate(await scope.ServiceProvider.GetRequiredService<ILeaseService>().CreateAsync(new LeaseCreation { RentalUnitId = unit, Start = new DateOnly(2024, 1, 1), TenantIds = [person], BaseRent = 500m, People = 1 }, Ct));
        meter = Validate(await scope.ServiceProvider.GetRequiredService<IMeterService>().CreateAsync(property, new MeterCreation
        {
            Data = new MeterInput { Type = MeterType.ColdWater, Name = "Kaltwasser", Number = "KW-1", AssignmentId = unit },
            InstalledOn = new DateOnly(2026, 1, 1),
            InitialReading = 100m,
        }, Ct));
        tenant = await LinkedAccountAsync(person);
    }

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    private static PortalReadingInput Input(decimal value, DateOnly? date = null) => new() { Date = date ?? new DateOnly(2026, 12, 16), Value = value };

    [Fact]
    public async Task The_tenant_sees_the_own_meters_and_a_report_waits_for_the_landlord()
    {
        var own = Assert.Single(await PortalAsync(p => p.MetersAsync(tenant, Ct)));
        Assert.Equal((100m, null), (own.LastValue, own.Pending));

        Validate(await PortalAsync(p => p.ReportReadingAsync(tenant, meter, Input(130m), Ct)));

        Assert.Equal((130m, new DateOnly(2026, 12, 16)), Assert.Single(await PortalAsync(p => p.MetersAsync(tenant, Ct))).Pending);
        Assert.Equal(100m, (await WithDbAsync(db => db.Set<Meter>().Include(m => m.Readings).SingleAsync(Ct))).Readings.Max(r => r.Value));
        Assert.Equal(130m, Assert.Single(await ReportsAsync(r => r.OpenAsync(property, Ct))).Value);
    }

    [Fact]
    public async Task Implausible_values_and_foreign_meters_are_refused()
    {
        Assert.False((await PortalAsync(p => p.ReportReadingAsync(tenant, meter, Input(90m), Ct))).Succeeded);
        Assert.False((await PortalAsync(p => p.ReportReadingAsync(tenant, meter, Input(120m, new DateOnly(2026, 12, 20)), Ct))).Succeeded);
        Assert.False((await PortalAsync(p => p.ReportReadingAsync(tenant, meter, Input(120m, new DateOnly(2026, 6, 1)), Ct))).Succeeded);
        var stranger = await LinkedAccountAsync(null);
        Assert.False((await PortalAsync(p => p.ReportReadingAsync(stranger, meter, Input(120m), Ct))).Succeeded);
        Assert.Empty(await PortalAsync(p => p.MetersAsync(stranger, Ct)));
        Assert.Empty(await ReportsAsync(r => r.OpenAsync(null, Ct)));
    }

    [Fact]
    public async Task A_new_report_replaces_the_open_one_and_accepting_records_the_reading()
    {
        Validate(await PortalAsync(p => p.ReportReadingAsync(tenant, meter, Input(130m), Ct)));
        Validate(await PortalAsync(p => p.ReportReadingAsync(tenant, meter, Input(135m), Ct)));
        var report = Assert.Single(await ReportsAsync(r => r.OpenAsync(null, Ct)));
        Assert.Equal(135m, report.Value);

        var accepted = await ReportsAsync(r => r.AcceptAsync(report.Id, false, Ct));
        Assert.True(accepted.Succeeded, string.Join(", ", accepted.Errors));

        Assert.Empty(await ReportsAsync(r => r.OpenAsync(null, Ct)));
        var stored = await WithDbAsync(db => db.Set<Meter>().Include(m => m.Readings).SingleAsync(Ct));
        Assert.Contains(stored.Readings, r => r.Value == 135m && r.Date == new DateOnly(2026, 12, 16));
        Assert.False((await ReportsAsync(r => r.AcceptAsync(report.Id, false, Ct))).Succeeded);
    }

    [Fact]
    public async Task A_rejected_report_records_nothing()
    {
        Validate(await PortalAsync(p => p.ReportReadingAsync(tenant, meter, Input(130m), Ct)));
        var report = Assert.Single(await ReportsAsync(r => r.OpenAsync(null, Ct)));

        Validate(await ReportsAsync(r => r.RejectAsync(report.Id, "Foto fehlt", Ct)));

        Assert.Empty(await ReportsAsync(r => r.OpenAsync(null, Ct)));
        Assert.Single((await WithDbAsync(db => db.Set<Meter>().Include(m => m.Readings).SingleAsync(Ct))).Readings);
    }

    [Fact]
    public async Task The_reminder_goes_out_once_a_year_from_the_15th_of_december_and_not_in_january()
    {
        Assert.Equal(1, await RemindAsync());
        Assert.Equal(0, await RemindAsync());
        environment.Clock.SetUtcNow(new DateTimeOffset(2027, 1, 10, 8, 0, 0, TimeSpan.Zero));
        Assert.Equal(0, await RemindAsync());

        var mail = Assert.Single(await WithDbAsync(db => db.Set<OutgoingMail>().Where(m => m.Template == "reading-reminder").ToListAsync(Ct)));
        Assert.Equal("anna@example.de", mail.To.Single());
        Assert.Contains("/portal/zaehler", mail.Html, StringComparison.Ordinal);
    }

    private async Task<int> RemindAsync()
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ReadingReminders>().SendAsync(Ct);
    }

    private async Task<T> PortalAsync<T>(Func<ITenantPortal, Task<T>> action)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<ITenantPortal>());
    }

    private async Task<T> ReportsAsync<T>(Func<IReadingReports, Task<T>> action)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IReadingReports>());
    }

    private async Task<T> WithDbAsync<T>(Func<ElocaroDbContext, Task<T>> action)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<ElocaroDbContext>());
    }

    private async Task<ClaimsPrincipal> LinkedAccountAsync(Guid? personId)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = $"{Guid.NewGuid():N}@example.de", Email = $"{Guid.NewGuid():N}@example.de", DisplayName = "Anna Test" };
        Assert.True((await manager.CreateAsync(user)).Succeeded);
        Assert.True((await manager.AddToRoleAsync(user, Roles.Tenant)).Succeeded);
        if (personId is { } id)
        {
            var db = scope.ServiceProvider.GetRequiredService<ElocaroDbContext>();
            (await db.Set<Person>().SingleAsync(p => p.Id == id, Ct)).UserId = user.Id;
            await db.SaveChangesAsync(Ct);
        }

        return new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Role, Roles.Tenant)], "test"));
    }

    private static Guid Validate(Result result)
    {
        Assert.True(result.Succeeded, string.Join(", ", result.Errors));
        return result.Id!.Value;
    }
}
