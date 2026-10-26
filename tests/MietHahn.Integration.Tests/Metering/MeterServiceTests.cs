using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Accounts;
using MietHahn.Application.Common;
using MietHahn.Application.Files;
using MietHahn.Application.Metering;
using MietHahn.Application.Properties;
using MietHahn.Application.Todos;
using MietHahn.Domain.Common;
using MietHahn.Domain.Metering;
using MietHahn.Infrastructure.Metering;
using MietHahn.Integration.Tests.Infrastructure;

namespace MietHahn.Integration.Tests.Metering;

public class MeterServiceTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateOnly YearEnd = new(2025, 12, 31);

    private InfrastructureEnvironment environment = null!;
    private Guid propertyId;
    private Guid apartment1;
    private Guid apartment2;
    private Guid main;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        environment = await InfrastructureEnvironment.CreateAsync(postgres);
        await using var scope = environment.Services.CreateAsyncScope();
        var properties = scope.ServiceProvider.GetRequiredService<IPropertyService>();
        propertyId = Validate(await properties.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct));
        apartment1 = Validate(await properties.CreateUnitAsync(propertyId, new UnitInput { Name = "Wohnung 1" }, Ct));
        apartment2 = Validate(await properties.CreateUnitAsync(propertyId, new UnitInput { Name = "Wohnung 2" }, Ct));
        main = await CreateAsync("Hauptwasserzähler", "HWZ-1", null, null, 1000m);
    }

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    [Fact]
    public async Task Meter_with_assignment_and_main_meter_in_reading_order()
    {
        var w1 = await CreateAsync("Kaltwasser Bad", "WZ-1", apartment1, main, 10m);
        var w2 = await CreateAsync("Kaltwasser Bad", "WZ-2", apartment2, main, 20m);

        var round = await WithAsync(v => v.RoundAsync(propertyId, null, Ct));
        Assert.Equal(["HWZ-1", "WZ-1", "WZ-2"], round.Select(z => z.Number));
        Assert.Equal(["Haus", "Wohnung 1", "Wohnung 2"], round.Select(z => z.Assignment));
        Assert.Equal(["WZ-2"], (await WithAsync(v => v.RoundAsync(propertyId, apartment2, Ct))).Select(z => z.Number));

        var details = (await WithAsync(v => v.GetAsync(main, Ct)))!;
        Assert.Equal([w1, w2], details.SubMeters.Select(u => u.Id));
    }

    [Fact]
    public async Task A_calculated_meter_has_the_consumption_of_its_source_meters_added_and_subtracted()
    {
        // Hauptwasserzähler "1. und 2. OG" (main) minus Zähler 2. OG = Wohnung 1
        var secondFloor = await CreateAsync("Kaltwasser 2. OG", "WZ-2OG", apartment2, null, 50m);
        await ReadingAsync(main, new DateOnly(2026, 12, 31), 1180m);
        await ReadingAsync(secondFloor, new DateOnly(2026, 12, 31), 100m);

        var firstFloor = Validate(await WithAsync(v => v.CreateAsync(
            propertyId,
            new MeterCreation
            {
                Data = new MeterInput { Type = MeterType.ColdWater, Name = "Kaltwasser 1. OG (berechnet)", Number = "RZ-1OG", AssignmentId = apartment1 },
                Terms = [new MeterTermInput(main, false), new MeterTermInput(secondFloor, true)],
            },
            Ct)));

        var consumption = (await WithAsync(v => v.ConsumptionAsync(firstFloor, Period.CalendarYear(2026), Ct)))!;

        // Hauptzähler 1,180 − 1,000 = 180 m³; 2. OG 100 − 50 = 50 m³ → 130 m³
        Assert.Equal(130m, consumption.Quantity);
        var details = (await WithAsync(v => v.GetAsync(firstFloor, Ct)))!;
        Assert.True(details.Meter.IsDerived);
        Assert.Null(details.Meter.LastReading);
        Assert.Equal([("Hauptwasserzähler (Nr. HWZ-1)", false), ("Kaltwasser 2. OG (Nr. WZ-2OG)", true)], details.Terms!.Select(t => (t.Label, t.Subtract)));

        // No own readings, no replacement, not in the reading round.
        var reading = await WithAsync(v => v.RecordReadingAsync(firstFloor, new ReadingInput { Date = new DateOnly(2026, 6, 1), Value = 5m, Confirmed = true }, null, Ct));
        Assert.False(reading.Succeeded);
        Assert.DoesNotContain(await WithAsync(v => v.RoundAsync(propertyId, null, Ct)), z => z.Id == firstFloor);
    }

    [Fact]
    public async Task A_calculated_meter_needs_two_meters_of_the_same_kind_and_at_least_one_to_add()
    {
        var other = await CreateAsync("Kaltwasser 2. OG", "WZ-2OG", apartment2, null, 50m);
        var heat = Validate(await WithAsync(v => v.CreateAsync(
            propertyId,
            new MeterCreation { Data = new MeterInput { Type = MeterType.Heat, Name = "Wärme", Number = "W-1" }, InstalledOn = YearEnd, InitialReading = 1m },
            Ct)));

        Task<Result> Derived(params MeterTermInput[] terms) => WithAsync(v => v.CreateAsync(
            propertyId,
            new MeterCreation { Data = new MeterInput { Type = MeterType.ColdWater, Name = "Berechnet", Number = "RZ-X" }, Terms = [.. terms] },
            Ct));

        Assert.False((await Derived(new MeterTermInput(main, false))).Succeeded);
        Assert.False((await Derived(new MeterTermInput(main, true), new MeterTermInput(other, true))).Succeeded);
        Assert.False((await Derived(new MeterTermInput(main, false), new MeterTermInput(heat, true))).Succeeded);
        Assert.True((await Derived(new MeterTermInput(main, false), new MeterTermInput(other, true))).Succeeded);
    }

    [Fact]
    public async Task Unusual_readings_require_confirmation()
    {
        var meter = await CreateAsync("Kaltwasser Bad", "WZ-1", apartment1, main, 10m);
        await ReadingAsync(meter, new DateOnly(2026, 12, 31), 375m);

        var backwards = await WithAsync(v => v.RecordReadingAsync(meter, new ReadingInput { Date = new DateOnly(2027, 2, 1), Value = 300m }, null, Ct));

        Assert.True(backwards.NeedsConfirmation);
        Assert.Contains("Rückwärtslauf", backwards.Warnings.Single(), StringComparison.Ordinal);
        Assert.Equal(2, (await WithAsync(v => v.GetAsync(meter, Ct)))!.Readings.Count);

        var confirmed = await WithAsync(v => v.RecordReadingAsync(meter, new ReadingInput { Date = new DateOnly(2027, 2, 1), Value = 300m, Confirmed = true, Note = "Zähler klemmte" }, null, Ct));
        Assert.True(confirmed.Succeeded);
        var lastReading = (await WithAsync(v => v.GetAsync(meter, Ct)))!.Readings[0];
        Assert.Equal(("system", "Zähler klemmte"), (lastReading.ReadBy, lastReading.Note));
    }

    [Fact]
    public async Task Reading_with_photo_as_proof()
    {
        var meter = await CreateAsync("Kaltwasser Bad", "WZ-1", apartment1, main, 10m);
        Guid photo;
        await using (var scope = environment.Services.CreateAsyncScope())
        {
            photo = (await scope.ServiceProvider.GetRequiredService<IFileStore>().SaveAsync(new MemoryStream(TestFiles.TwoColorImage(40, 30)), "meter.jpg", Ct)).Id;
        }

        Assert.True((await WithAsync(v => v.RecordReadingAsync(meter, new ReadingInput { Date = new DateOnly(2026, 6, 30), Value = 190m }, photo, Ct))).Succeeded);

        Assert.Equal(photo, (await WithAsync(v => v.GetAsync(meter, Ct)))!.Readings[0].PhotoFileId);
    }

    [Fact]
    public async Task Meter_replacement_keeps_consumption_continuous_and_sub_meters_follow()
    {
        var w1 = await CreateAsync("Kaltwasser Bad", "WZ-1", apartment1, main, 10m);
        await ReadingAsync(w1, new DateOnly(2026, 12, 31), 110m);
        await ReadingAsync(main, new DateOnly(2026, 6, 1), 1200m);

        var newMain = Validate(await WithAsync(v => v.ReplaceAsync(main, new ReplacementInput { Date = new DateOnly(2026, 7, 1), FinalReading = 1240m, NewNumber = "HWZ-2", InitialReading = 0m }, Ct)));
        await ReadingAsync(newMain, new DateOnly(2026, 12, 31), 260m);

        var consumption = (await WithAsync(v => v.ConsumptionAsync(newMain, Period.CalendarYear(2026), Ct)))!;
        Assert.Equal(240m + 260m, consumption.Quantity);
        Assert.Equal(consumption.Quantity, (await WithAsync(v => v.ConsumptionAsync(main, Period.CalendarYear(2026), Ct)))!.Quantity);

        var details = (await WithAsync(v => v.GetAsync(newMain, Ct)))!;
        Assert.Equal(["HWZ-1", "HWZ-2"], details.MeteringPoint.Select(m => m.Number));
        Assert.Equal(w1, details.SubMeters.Single().Id);
        Assert.Equal(["HWZ-2", "WZ-1"], (await WithAsync(v => v.ListAsync(propertyId, false, Ct))).Select(z => z.Number));
        Assert.Equal(3, (await WithAsync(v => v.ListAsync(propertyId, true, Ct))).Count);
    }

    [Fact]
    public async Task Difference_between_main_meter_and_sum_of_sub_meters()
    {
        var w1 = await CreateAsync("Kaltwasser Bad", "WZ-1", apartment1, main, 10m);
        var w2 = await CreateAsync("Kaltwasser Bad", "WZ-2", apartment2, main, 20m);
        // The balance shows the previous year: clock at February 2027 → year 2026
        environment.Clock.SetUtcNow(new DateTimeOffset(2027, 2, 1, 10, 0, 0, TimeSpan.Zero));
        await ReadingAsync(main, new DateOnly(2026, 12, 31), 1500m);
        await ReadingAsync(w1, new DateOnly(2026, 12, 31), 210m);
        await ReadingAsync(w2, new DateOnly(2026, 12, 31), 260m);

        var balance = (await WithAsync(v => v.GetAsync(main, Ct)))!.Balance!;

        Assert.Equal(2026, balance.Year);
        Assert.Equal((500m, 440m, 60m), (balance.MainMeter!.Value, balance.SubMeterTotal!.Value, balance.Difference!.Value));
        Assert.Empty(balance.Hints);
    }

    [Fact]
    public async Task Assignment_and_main_meter_must_match_the_property()
    {
        await using var scope = environment.Services.CreateAsyncScope();
        var properties = scope.ServiceProvider.GetRequiredService<IPropertyService>();
        var other = Validate(await properties.CreateAsync(new PropertyInput { Name = "Birkenallee 9", Street = "Birkenallee 9", PostalCode = "50668", City = "Köln" }, Ct));
        var otherFlat = Validate(await properties.CreateUnitAsync(other, new UnitInput { Name = "Wohnung A" }, Ct));

        var wrongAssignment = await WithAsync(v => v.CreateAsync(propertyId, Creation("X", "X-1", otherFlat, null, 0m), Ct));
        var wrongType = await WithAsync(v => v.CreateAsync(propertyId, new MeterCreation
        {
            Data = new MeterInput { Type = MeterType.Electricity, Name = "Strom", Number = "S-1", MainMeterId = main },
            InstalledOn = YearEnd,
            InitialReading = 0m,
        }, Ct));

        Assert.Equal("Die Zuordnung gehört nicht zu diesem Objekt.", wrongAssignment.Errors.Single());
        Assert.Contains("dasselbe messen", wrongType.Errors.Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Expiring_calibration_dates_appear_on_the_dashboard()
    {
        // Installed end of 2019 → cold water calibration deadline 31 Dec 2025, expired at test clock 1 March 2026
        var old = Validate(await WithAsync(v => v.CreateAsync(propertyId, new MeterCreation
        {
            Data = new MeterInput { Type = MeterType.ColdWater, Name = "Alter Zähler", Number = "ALT-1" },
            InstalledOn = new DateOnly(2019, 12, 1),
            InitialReading = 0m,
        }, Ct)));

        await using var scope = environment.Services.CreateAsyncScope();
        var source = scope.ServiceProvider.GetServices<ITodoSource>().OfType<CalibrationDueTodos>().Single();
        var todo = Assert.Single(await source.TodosAsync(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, Roles.Admin)], "test")), Ct));

        Assert.Equal("Eichfrist abgelaufen: Alter Zähler (ALT-1)", todo.Title);
        Assert.Equal($"/verwaltung/zaehler/{old}", todo.Link);
        Assert.Equal(Urgency.Urgent, todo.Urgency);
    }

    private async Task<Guid> CreateAsync(string name, string number, Guid? assignment, Guid? mainMeter, decimal start) =>
        Validate(await WithAsync(v => v.CreateAsync(propertyId, Creation(name, number, assignment, mainMeter, start), Ct)));

    private static MeterCreation Creation(string name, string number, Guid? assignment, Guid? mainMeter, decimal start) => new()
    {
        Data = new MeterInput { Type = MeterType.ColdWater, Name = name, Number = number, AssignmentId = assignment, MainMeterId = mainMeter },
        InstalledOn = YearEnd,
        InitialReading = start,
    };

    private async Task ReadingAsync(Guid meter, DateOnly date, decimal value)
    {
        var result = await WithAsync(v => v.RecordReadingAsync(meter, new ReadingInput { Date = date, Value = value, Confirmed = true }, null, Ct));
        Assert.True(result.Succeeded, string.Join(", ", result.Errors));
    }

    private static Guid Validate(Result result)
    {
        Assert.True(result.Succeeded, string.Join(", ", result.Errors));
        return result.Id!.Value;
    }

    private async Task<T> WithAsync<T>(Func<IMeterService, Task<T>> action)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IMeterService>());
    }
}
