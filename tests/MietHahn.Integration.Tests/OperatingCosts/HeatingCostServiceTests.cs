using Microsoft.Extensions.DependencyInjection;
using MietHahn.Domain.Receipts;
using MietHahn.Application.Common;
using MietHahn.Application.Finance;
using MietHahn.Application.Heating;
using MietHahn.Application.Leases;
using MietHahn.Application.Metering;
using MietHahn.Application.OperatingCosts;
using MietHahn.Application.People;
using MietHahn.Application.Properties;
using MietHahn.Application.Receipts;
using MietHahn.Domain.Common;
using MietHahn.Domain.Heating;
using MietHahn.Domain.Metering;
using MietHahn.Domain.OperatingCosts;
using MietHahn.Integration.Tests.Infrastructure;

namespace MietHahn.Integration.Tests.OperatingCosts;

/// <summary>
/// Heating costs (HeizkostenV) and CO₂ cost split from stored data: receipts with fuel quantity and CO₂, heat and hot water meters,
/// the heat meter in the hot water circuit, interim readings, oil tank stock and the settings of the property.
/// </summary>
public class HeatingCostServiceTests(PostgresFixture postgres) : IAsyncLifetime
{
    private InfrastructureEnvironment environment = null!;
    private Guid house;
    private Guid apartment1;
    private Guid apartment2;
    private Guid leaseAnna;
    private Guid leaseBen;
    private Guid leaseClara;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        environment = await InfrastructureEnvironment.CreateAsync(postgres);
        environment.Clock.SetUtcNow(new DateTimeOffset(2027, 1, 20, 10, 0, 0, TimeSpan.Zero));
        await using var scope = environment.Services.CreateAsyncScope();
        var properties = scope.ServiceProvider.GetRequiredService<IPropertyService>();
        var people = scope.ServiceProvider.GetRequiredService<IPersonService>();
        var leases = scope.ServiceProvider.GetRequiredService<ILeaseService>();

        house = Validate(await properties.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct));
        apartment1 = Validate(await properties.CreateUnitAsync(house, new UnitInput { Name = "Wohnung 1" }, Ct));
        apartment2 = Validate(await properties.CreateUnitAsync(house, new UnitInput { Name = "Wohnung 2" }, Ct));
        Validate(await properties.SetUnitAreaAsync(apartment1, new AreaInput { From = new DateOnly(2020, 1, 1), Area = 60m }, Ct));
        Validate(await properties.SetUnitAreaAsync(apartment2, new AreaInput { From = new DateOnly(2020, 1, 1), Area = 40m }, Ct));

        var anna = Validate(await people.CreateAsync(new PersonInput { FirstName = "Anna", LastName = "Schmidt" }, null, Ct));
        var ben = Validate(await people.CreateAsync(new PersonInput { FirstName = "Ben", LastName = "Meier" }, null, Ct));
        var clara = Validate(await people.CreateAsync(new PersonInput { FirstName = "Clara", LastName = "Vogel" }, null, Ct));
        leaseAnna = Validate(await leases.CreateAsync(new LeaseCreation { RentalUnitId = apartment1, Start = new DateOnly(2025, 1, 1), TenantIds = [anna], BaseRent = 500m, OperatingCostPrepayment = 100m, HeatingCostPrepayment = 40m, People = 2 }, Ct));
        leaseBen = Validate(await leases.CreateAsync(new LeaseCreation { RentalUnitId = apartment2, Start = new DateOnly(2025, 1, 1), TenantIds = [ben], BaseRent = 500m, OperatingCostPrepayment = 50m }, Ct));
        Validate(await leases.MoveOutAsync(leaseBen, new MoveOutInput { End = new DateOnly(2026, 6, 14) }, Ct));
        leaseClara = Validate(await leases.CreateAsync(new LeaseCreation { RentalUnitId = apartment2, Start = new DateOnly(2026, 6, 15), TenantIds = [clara], BaseRent = 500m, OperatingCostPrepayment = 50m }, Ct));
    }

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    [Fact]
    public async Task Heating_costs_from_receipts_meters_and_settings_with_carbon_split_and_prepayments()
    {
        await StartAccountingAsync();
        await GasReceiptAsync();
        var wwMeter = await MeterAsync(null, MeterType.Heat, "WW-WMZ", 0m, "Wärmezähler Warmwasser");
        await ReadingAsync(wwMeter, new DateOnly(2026, 12, 31), 2400m);
        var heat1 = await MeterAsync(apartment1, MeterType.Heat, "WMZ-1", 0m, unit: MeasurementUnit.MegawattHours);
        await ReadingAsync(heat1, new DateOnly(2026, 12, 31), 4m); // 4 MWh = 4,000 kWh
        var heat2 = await MeterAsync(apartment2, MeterType.Heat, "WMZ-2", 0m);
        await ReadingAsync(heat2, new DateOnly(2026, 6, 14), 1500m); // interim reading on the tenant change
        await ReadingAsync(heat2, new DateOnly(2026, 12, 31), 3000m);
        var water1 = await MeterAsync(apartment1, MeterType.HotWater, "WW-1", 0m);
        await ReadingAsync(water1, new DateOnly(2026, 12, 31), 30m);
        var water2 = await MeterAsync(apartment2, MeterType.HotWater, "WW-2", 0m);
        await ReadingAsync(water2, new DateOnly(2026, 6, 14), 12m);
        await ReadingAsync(water2, new DateOnly(2026, 12, 31), 30m);
        Validate(await SaveSettingsAsync(new HeatingSettingsInput { ConsumptionPercent = 60m, JointPlant = true, HotWaterHeatMeterId = wwMeter, ApplyCarbonCosts = true }));

        var preview = await PreviewAsync();

        var result = preview.Result;
        Assert.False(result.HasErrors, string.Join("; ", result.Problems.Select(p => p.Text)));
        Assert.Empty(result.UncalculatedGroups);

        // Total costs 1,350.00 € (gas 1,200.00 + CO₂ 150.00). § 9: 2,400 kWh (heat meter hot water) ÷ 12,000 kWh = 20 % → hot water 270.00 €, heating 1,080.00 €.
        // Heating 60 % by kWh (Anna 4,000, Ben 1,500, Clara 1,500) and 40 % by area×days (60×365 : 40×165 : 40×200 = 21,900 : 6,600 : 8,000):
        //   Anna 648 × 4/7 + 432 × 21,900/36,500 = 370.29 + 259.20 = 629.49    Ben 138.86 + 78.12 = 216.97    Clara 138.86 + 94.68 = 233.54
        // Hot water 60 % by m³ (30 : 12 : 18) and 40 % by living area: Anna 81.00 + 64.80 = 145.80    Ben 32.40 + 19.53 = 51.93    Clara 48.60 + 23.67 = 72.27
        Assert.Equal(1080m, result.CostTypes.Single(t => t.CostType == HeatingCostCalculator.HeatKey).Total);
        Assert.Equal(270m, result.CostTypes.Single(t => t.CostType == HeatingCostCalculator.HotWaterKey).Total);
        Assert.Equal([629.49m, 216.97m, 233.54m], new[] { leaseAnna, leaseBen, leaseClara }.Select(l => Share(result, l, HeatingCostCalculator.HeatKey)));
        Assert.Equal([145.80m, 51.93m, 72.27m], new[] { leaseAnna, leaseBen, leaseClara }.Select(l => Share(result, l, HeatingCostCalculator.HotWaterKey)));

        // CO₂: 2,400 kg ÷ 100 m² = 24 kg/(m²·a) → level 4, landlord 30 %. CO₂ costs 150.00 € × 30 % = 45.00 € deducted: 25.84 / 8.96 / 10.20.
        Assert.Equal([-25.84m, -8.96m, -10.20m], new[] { leaseAnna, leaseBen, leaseClara }.Select(l => Share(result, l, HeatingCostCalculator.CarbonKey)));
        var carbon = result.CostTypes.Single(t => t.CostType == HeatingCostCalculator.CarbonKey);
        Assert.Equal(45m, carbon.LandlordTotal);
        Assert.Contains("Stufe 4", carbon.Key, StringComparison.Ordinal);

        // Heating prepayments (Anna 40.00 € × 12 = 480.00 € target, nothing paid): net costs 749.45 €
        var anna = result.Tenants.Single(t => t.LeaseId == leaseAnna).Groups.Single(g => g.Group == CostGroup.Heating);
        Assert.True(anna.Calculated);
        Assert.Equal((749.45m, 0m, 480m, 749.45m), (anna.Share, anna.PrepaymentsPaid, anna.PrepaymentsDue, anna.Balance));
        Assert.Contains(result.Problems, p => p.Text.Contains("Stufentabelle", StringComparison.Ordinal) && p.Text.Contains("Stand/prüfen", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Problems, p => p.Text.Contains("Gradtagszahlen", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Tenant_change_without_interim_reading_uses_the_degree_day_table()
    {
        await GasReceiptAsync();
        var heat1 = await MeterAsync(apartment1, MeterType.Heat, "WMZ-1", 0m);
        await ReadingAsync(heat1, new DateOnly(2026, 12, 31), 4000m);
        var heat2 = await MeterAsync(apartment2, MeterType.Heat, "WMZ-2", 0m);
        await ReadingAsync(heat2, new DateOnly(2026, 12, 31), 3000m);
        var water1 = await MeterAsync(apartment1, MeterType.HotWater, "WW-1", 0m);
        await ReadingAsync(water1, new DateOnly(2026, 12, 31), 30m);
        var water2 = await MeterAsync(apartment2, MeterType.HotWater, "WW-2", 0m);
        await ReadingAsync(water2, new DateOnly(2026, 12, 31), 30m);
        Validate(await SaveSettingsAsync(new HeatingSettingsInput { ConsumptionPercent = 70m, JointPlant = true, ApplyCarbonCosts = true }));

        var result = (await PreviewAsync()).Result;

        // Wohnung 2 used 3,000 kWh without a reading on 14.06.: January to May 540 ‰ + June 1 to 14 = 10 ‰ × 14/30 = 4.667 ‰ → Ben 54.4667 % = 1,634 kWh
        Assert.Contains(result.Problems, p => p.Severity == ProblemSeverity.Warning && p.Text.Contains("Wohnung 2 ohne Zwischenablesung", StringComparison.Ordinal) && p.Text.Contains("Gradtagszahlen", StringComparison.Ordinal));
        var ben = result.Tenants.Single(t => t.LeaseId == leaseBen).Lines.Single(l => l.CostType == HeatingCostCalculator.HeatKey);
        Assert.Contains("1.634 von 7.000 kWh", ben.Basis, StringComparison.Ordinal);
        Assert.Contains(ben.Protocol, e => e.Note is not null && e.Note.Contains("Gradtagszahlen 54,47 % von 3.000 kWh", StringComparison.Ordinal));

        // No formula without a heat meter in the hot water circuit: the formula for 60 m³ hot water applies.
        Assert.Contains(result.Sections.Single(s => s.Id == "heating").Entries, e => e.Values.Contains("Formel Q = 2,5 × V × (t − 10)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Oil_is_valued_from_the_tank_stock_and_the_deliveries()
    {
        var tank = await MeterAsync(null, MeterType.OilTank, "TANK-1", 1000m, "Öltank");
        await ReadingAsync(tank, new DateOnly(2026, 12, 31), 1200m);
        await OilReceiptAsync(new DateOnly(2026, 3, 15), 2000m, 1900m);
        await OilReceiptAsync(new DateOnly(2026, 9, 20), 1500m, 1500m);
        foreach (var unit in new[] { apartment1, apartment2 })
        {
            var heat = await MeterAsync(unit, MeterType.Heat, $"WMZ-{unit:N}"[..12], 0m);
            await ReadingAsync(heat, new DateOnly(2026, 12, 31), 2000m);
        }

        Validate(await SaveSettingsAsync(new HeatingSettingsInput
        {
            Fuel = HeatingFuel.Oil,
            ConsumptionPercent = 70m,
            JointPlant = false,
            OilTankMeterId = tank,
            OilValuation = OilValuation.Fifo,
            OilOpeningPricePerLiter = 0.90m,
            ApplyCarbonCosts = true,
        }));

        var result = (await PreviewAsync()).Result;

        // 1,000 l (0.90 €/l) + 3,500 l − 1,200 l = 3,300 l; FIFO: 1,000 × 0.90 + 2,000 × 0.95 + 300 × 1.00 = 3,100.00 €
        Assert.Equal(3100m, result.CostTypes.Single(t => t.CostType == HeatingCostCalculator.HeatKey).Total);
        Assert.DoesNotContain(result.Problems, p => p.Severity == ProblemSeverity.Error && p.Text.Contains("Tank", StringComparison.Ordinal));
        var energy = result.Sections.Single(s => s.Id == "heating").Entries.Single(e => e.Title == "Brennstoffenergie");
        Assert.Equal(33000m, energy.Result);
    }

    [Fact]
    public async Task Missing_oil_tank_is_reported()
    {
        await OilReceiptAsync(new DateOnly(2026, 3, 15), 2000m, 1900m);
        Validate(await SaveSettingsAsync(new HeatingSettingsInput { Fuel = HeatingFuel.Oil, ConsumptionPercent = 70m, JointPlant = false, ApplyCarbonCosts = true }));

        var result = (await PreviewAsync()).Result;

        Assert.True(result.HasErrors);
        Assert.Contains(result.Problems, p => p.Severity == ProblemSeverity.Error && p.Text.Contains("kein Tankzähler gewählt", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Settings_default_to_70_30_and_gas_and_are_validated_when_saved()
    {
        var defaults = (await WithAsync<IHeatingSettings, HeatingSettingsView?>(s => s.GetAsync(house, Ct)))!;
        Assert.False(defaults.Saved);
        Assert.Equal((HeatingFuel.Gas, 70m, 2.5m, 60m, true, true), (defaults.Input.Fuel, defaults.Input.ConsumptionPercent, defaults.Input.HotWaterFormulaFactor, defaults.Input.HotWaterTemperature, defaults.Input.JointPlant, defaults.Input.ApplyCarbonCosts));
        Assert.Contains("Stand/prüfen", defaults.CarbonTableStatus, StringComparison.Ordinal);
        Assert.Contains("Stand/prüfen", defaults.DegreeDayTableStatus, StringComparison.Ordinal);

        foreach (var invalid in new[] { 49m, 71m, 100m })
        {
            var failed = await SaveSettingsAsync(new HeatingSettingsInput { ConsumptionPercent = invalid, JointPlant = true });
            Assert.False(failed.Succeeded);
            Assert.Contains("zwischen 50 und 70", failed.Errors.Single(), StringComparison.Ordinal);
        }

        var coldMeter = await MeterAsync(null, MeterType.ColdWater, "KW-1", 0m);
        var wrongMeter = await SaveSettingsAsync(new HeatingSettingsInput { ConsumptionPercent = 50m, HotWaterHeatMeterId = coldMeter });
        Assert.False(wrongMeter.Succeeded);

        var heatMeter = await MeterAsync(null, MeterType.Heat, "WW-WMZ", 0m, "Wärmezähler Warmwasser");
        Validate(await SaveSettingsAsync(new HeatingSettingsInput
        {
            Fuel = HeatingFuel.Gas,
            ConsumptionPercent = 50m,
            JointPlant = false,
            HotWaterHeatMeterId = heatMeter,
            HotWaterFormulaFactor = 2.6m,
            HotWaterTemperature = 55m,
            ApplyCarbonCosts = false,
            CarbonSpecialCase = CarbonSpecialCase.ProtectedBuilding,
            CarbonNote = "  Denkmalschutz seit 1990  ",
        }));

        var saved = (await WithAsync<IHeatingSettings, HeatingSettingsView?>(s => s.GetAsync(house, Ct)))!;
        Assert.True(saved.Saved);
        Assert.Equal((50m, false, heatMeter, 2.6m, 55m, false, CarbonSpecialCase.ProtectedBuilding, "Denkmalschutz seit 1990"), (saved.Input.ConsumptionPercent, saved.Input.JointPlant, saved.Input.HotWaterHeatMeterId, saved.Input.HotWaterFormulaFactor, saved.Input.HotWaterTemperature, saved.Input.ApplyCarbonCosts, saved.Input.CarbonSpecialCase, saved.Input.CarbonNote));
        Assert.Contains(saved.HeatMeters, m => m.Id == heatMeter);

        // Saving again changes the one row.
        Validate(await SaveSettingsAsync(new HeatingSettingsInput { ConsumptionPercent = 65m, JointPlant = true, ApplyCarbonCosts = true }));
        Assert.Equal(65m, (await WithAsync<IHeatingSettings, HeatingSettingsView?>(s => s.GetAsync(house, Ct)))!.Input.ConsumptionPercent);

        // Brennwert and Zustandszahl per year are kept and sorted.
        Validate(await SaveSettingsAsync(new HeatingSettingsInput
        {
            ConsumptionPercent = 65m,
            Fuel = HeatingFuel.Gas,
            FuelKwhPerUnit = 10m,
            EnergyYears = [new FuelEnergyYearInput { Year = 2026, CalorificValue = 11.2m, StateNumber = 0.95m, Note = "Jahresrechnung" }, new FuelEnergyYearInput { Year = 2025, CalorificValue = 11m, StateNumber = 0.96m }],
        }));
        var years = (await WithAsync<IHeatingSettings, HeatingSettingsView?>(s => s.GetAsync(house, Ct)))!.Input.EnergyYears;
        Assert.Equal([2025, 2026], years.Select(e => e.Year));
        Assert.Equal("Jahresrechnung", years[1].Note);

        Assert.Null(await WithAsync<IHeatingSettings, HeatingSettingsView?>(s => s.GetAsync(Guid.NewGuid(), Ct)));
        Assert.False((await WithAsync<IHeatingSettings, Result>(s => s.SaveAsync(Guid.NewGuid(), new HeatingSettingsInput { ConsumptionPercent = 70m }, Ct))).Succeeded);
    }

    [Fact]
    public async Task Heating_kind_of_a_cost_item_is_stored_for_heating_cost_types_only()
    {
        var receipt = await GasReceiptAsync();

        var details = (await WithAsync<IReceiptService, ReceiptDetails?>(s => s.GetAsync(receipt, Ct)))!;
        Assert.Equal([HeatingCostKind.Fuel, HeatingCostKind.CarbonPrice], details.Items.OrderBy(i => i.Amount < 200 ? 1 : 0).Select(i => i.HeatingKind));

        var other = Validate(await WithAsync<IReceiptService, Result>(s => s.CreateAsync(
            new ReceiptInput { SupplierName = "Stadt Köln", InvoiceDate = new DateOnly(2026, 12, 1), Gross = 100m },
            [],
            new CostItemInput { CostType = "betrkv-01", PropertyId = house, HeatingKind = HeatingCostKind.Maintenance, From = new DateOnly(2026, 1, 1), To = new DateOnly(2026, 12, 31) },
            Ct)));
        Assert.Null((await WithAsync<IReceiptService, ReceiptDetails?>(s => s.GetAsync(other, Ct)))!.Items.Single().HeatingKind);
    }

    // ---- helpers ---------------------------------------------------------------------------------------------------

    private static decimal Share(BillingResult result, Guid lease, string type) =>
        result.Tenants.Single(t => t.LeaseId == lease).Lines.Single(l => l.CostType == type).Share;

    private static Guid Validate(Result result)
    {
        Assert.True(result.Succeeded, string.Join(", ", result.Errors));
        return result.Id!.Value;
    }

    private async Task<TResult> WithAsync<TService, TResult>(Func<TService, Task<TResult>> action)
        where TService : notnull
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }

    private Task<OperatingCostPreview> PreviewAsync() =>
        WithAsync<IOperatingCosts, OperatingCostPreview>(async s => (await s.PreviewAsync(house, 2026, Ct))!);

    private Task<Result> SaveSettingsAsync(HeatingSettingsInput input) =>
        WithAsync<IHeatingSettings, Result>(s => s.SaveAsync(house, input, Ct));

    private async Task StartAccountingAsync()
    {
        await WithAsync<IFinanceSettings, bool>(async s =>
        {
            await s.SaveAsync(new FinanceSettingsInput { AccountingStart = new DateOnly(2026, 1, 1) }, Ct);
            return true;
        });
        await WithAsync<ITenantAccounts, int>(s => s.GenerateChargesAsync(null, Ct));
    }

    /// <summary>Gas bill 1,350.00 € for 12,000 kWh and 2,400 kg CO₂: 1,200.00 € gas and 150.00 € CO₂ costs of the supplier.</summary>
    private async Task<Guid> GasReceiptAsync()
    {
        var receipt = Validate(await WithAsync<IReceiptService, Result>(s => s.CreateAsync(
            new ReceiptInput { SupplierName = "Stadtwerke", InvoiceNumber = "G-2026", InvoiceDate = new DateOnly(2026, 12, 15), Gross = 1350m, FuelQuantity = 12000m, FuelUnit = "kWh", Co2Kg = 2400m },
            [],
            new CostItemInput { CostType = "betrkv-06", PropertyId = house, Amount = 1200m, HeatingKind = HeatingCostKind.Fuel, From = new DateOnly(2026, 1, 1), To = new DateOnly(2026, 12, 31) },
            Ct)));
        Validate(await WithAsync<IReceiptService, Result>(s => s.AddItemAsync(
            receipt,
            new CostItemInput { CostType = "betrkv-06", PropertyId = house, Amount = 150m, HeatingKind = HeatingCostKind.CarbonPrice, From = new DateOnly(2026, 1, 1), To = new DateOnly(2026, 12, 31) },
            Ct)));
        return receipt;
    }

    private async Task OilReceiptAsync(DateOnly date, decimal liters, decimal amount) =>
        Validate(await WithAsync<IReceiptService, Result>(s => s.CreateAsync(
            new ReceiptInput { SupplierName = "Ölhandel", InvoiceDate = date, Gross = amount, FuelQuantity = liters, FuelUnit = "l" },
            [],
            new CostItemInput { CostType = "betrkv-04", PropertyId = house, HeatingKind = HeatingCostKind.Fuel, From = date, To = date },
            Ct)));

    private async Task<Guid> MeterAsync(Guid? assignment, MeterType type, string number, decimal initial, string? name = null, MeasurementUnit? unit = null) =>
        Validate(await WithAsync<IMeterService, Result>(s => s.CreateAsync(
            house,
            new MeterCreation
            {
                Data = new MeterInput { Type = type, Name = name ?? type.ToString(), Number = number, AssignmentId = assignment, MeasurementUnit = unit },
                InstalledOn = new DateOnly(2025, 12, 31),
                InitialReading = initial,
            },
            Ct)));

    private async Task ReadingAsync(Guid meter, DateOnly date, decimal value)
    {
        var result = await WithAsync<IMeterService, ReadingResult>(s => s.RecordReadingAsync(meter, new ReadingInput { Date = date, Value = value, Confirmed = true }, null, Ct));
        Assert.True(result.Succeeded, string.Join(", ", result.Errors));
    }
}
