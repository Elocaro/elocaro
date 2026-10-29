using Elocaro.Application.Common;
using Elocaro.Application.Finance;
using Elocaro.Application.Heating;
using Elocaro.Application.Leases;
using Elocaro.Application.Metering;
using Elocaro.Application.OperatingCosts;
using Elocaro.Application.People;
using Elocaro.Application.Properties;
using Elocaro.Application.Receipts;
using Elocaro.Domain.Common;
using Elocaro.Domain.Heating;
using Elocaro.Domain.Leases;
using Elocaro.Domain.Metering;
using Elocaro.Domain.OperatingCosts;
using Elocaro.Domain.Receipts;
using Elocaro.Integration.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Integration.Tests.OperatingCosts;

/// <summary>Preview of the operating cost statement from stored data: receipts, keys, leases, meters and the tenant account.</summary>
public class OperatingCostServiceTests(PostgresFixture postgres) : IAsyncLifetime
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

        leaseAnna = Validate(await leases.CreateAsync(new LeaseCreation { RentalUnitId = apartment1, Start = new DateOnly(2025, 1, 1), TenantIds = [anna], BaseRent = 500m, OperatingCostPrepayment = 100m, People = 2 }, Ct));
        leaseBen = Validate(await leases.CreateAsync(new LeaseCreation { RentalUnitId = apartment2, Start = new DateOnly(2025, 1, 1), TenantIds = [ben], BaseRent = 500m, OperatingCostPrepayment = 50m }, Ct));
        Validate(await leases.MoveOutAsync(leaseBen, new MoveOutInput { End = new DateOnly(2026, 6, 14) }, Ct));
        leaseClara = Validate(await leases.CreateAsync(new LeaseCreation { RentalUnitId = apartment2, Start = new DateOnly(2026, 6, 15), TenantIds = [clara], BaseRent = 500m, OperatingCostPrepayment = 50m }, Ct));
    }

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    [Fact]
    public async Task Preview_from_stored_receipts_keys_and_actually_paid_prepayments()
    {
        await StartAccountingAsync();
        await ReceiptAsync("Stadt Köln", "betrkv-01", 1000m);
        await SetRuleAsync("betrkv-01", AllocationKey.LivingArea);
        await PayAsync();

        var preview = (await WithAsync<IOperatingCosts, OperatingCostPreview?>(s => s.PreviewAsync(house, 2026, Ct)))!;

        Assert.Equal("Lindenweg 3", preview.PropertyName);
        Assert.Empty(preview.Hints);
        var result = preview.Result;
        Assert.False(result.HasErrors);
        Assert.Equal(1000m, result.CostTypes.Single().Total);

        // Σ m²·days: Anna 60 × 365 = 21,900; Ben 40 × 165 = 6,600; Clara 40 × 200 = 8,000 → 36,500; 1,000.00 € → 600.00 / 180.82 / 219.18
        Assert.Equal([600m, 180.82m, 219.18m], new[] { leaseAnna, leaseBen, leaseClara }.Select(l => Share(result, l, "betrkv-01")));
        Assert.Equal(["Anna Schmidt", "Ben Meier", "Clara Vogel"], result.Tenants.OrderBy(t => t.Term.From).Select(t => t.Label));
        Assert.Equal(["Wohnung 1", "Wohnung 2", "Wohnung 2"], result.Tenants.OrderBy(t => t.Term.From).Select(t => t.Rental));

        // Prepayments: what was actually paid, not the target. Anna: 11 of 12 months (December unpaid) = 1,100.00 € of 1,200.00 €;
        // Ben: 5 × 50.00 + June 14/30 × 50.00 = 23.33 → 273.33 €; Clara: nothing paid (target: June 16/30 × 50.00 = 26.67 + 6 × 50.00 = 326.67 €).
        var anna = result.Tenants.Single(t => t.LeaseId == leaseAnna).Operating!;
        var ben = result.Tenants.Single(t => t.LeaseId == leaseBen).Operating!;
        var clara = result.Tenants.Single(t => t.LeaseId == leaseClara).Operating!;
        Assert.Equal((1100m, 1200m), (anna.PrepaymentsPaid, anna.PrepaymentsDue));
        Assert.Equal((273.33m, 273.33m), (ben.PrepaymentsPaid, ben.PrepaymentsDue));
        Assert.Equal((0m, 326.67m), (clara.PrepaymentsPaid, clara.PrepaymentsDue));
        Assert.Equal([-500m, -92.51m, 219.18m], new[] { anna, ben, clara }.Select(g => g.Balance));
        Assert.Equal(2, result.Problems.Count(p => p.Severity == ProblemSeverity.Warning && p.Text.Contains("noch offen", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Consumption_key_uses_meters_with_interim_reading_and_marks_interpolation()
    {
        await StartAccountingAsync();
        await ReceiptAsync("Stadtwerke", "betrkv-02", 732m);
        await SetRuleAsync("betrkv-02", AllocationKey.Consumption, meter: MeterType.ColdWater);
        var meter1 = await MeterAsync(apartment1, "W-1", new DateOnly(2025, 12, 31), 0m);
        var meter2 = await MeterAsync(apartment2, "W-2", new DateOnly(2025, 12, 31), 0m);
        await ReadingAsync(meter1, new DateOnly(2026, 12, 31), 100m);
        await ReadingAsync(meter2, new DateOnly(2026, 6, 14), 33m); // interim reading on the tenant change
        await ReadingAsync(meter2, new DateOnly(2026, 12, 31), 83m);

        var result = (await PreviewAsync()).Result;

        // 183 m³ in total (Wohnung 1: 100, Wohnung 2: Ben 33 + Clara 50); 732.00 € ÷ 183 = 4.00 €/m³
        Assert.False(result.HasErrors);
        Assert.Equal([400m, 132m, 200m], new[] { leaseAnna, leaseBen, leaseClara }.Select(l => Share(result, l, "betrkv-02")));

        // Wohnung 1 has no interim reading: split at the tenant change by interpolation, which the protocol shows.
        var annaNote = result.Tenants.Single(t => t.LeaseId == leaseAnna).Lines.Single().Protocol[1].Note!;
        Assert.Contains("interpolierter Zählerstand", annaNote, StringComparison.Ordinal);
        Assert.DoesNotContain("interpolierter", result.Tenants.Single(t => t.LeaseId == leaseClara).Lines.Single().Protocol[1].Note!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Consumption_key_uses_a_calculated_meter_for_a_unit_without_its_own_meter()
    {
        await StartAccountingAsync();
        await ReceiptAsync("Stadtwerke", "betrkv-02", 732m);
        await SetRuleAsync("betrkv-02", AllocationKey.Consumption, meter: MeterType.ColdWater);
        // Hauptzähler of the house counts both flats; only Wohnung 2 has its own meter: Wohnung 1 = Hauptzähler − Zähler Wohnung 2.
        var houseMeter = await MeterAsync(null, "H-1", new DateOnly(2025, 12, 31), 0m);
        var meter2 = await MeterAsync(apartment2, "W-2", new DateOnly(2025, 12, 31), 0m);
        await ReadingAsync(houseMeter, new DateOnly(2026, 6, 14), 100m);
        await ReadingAsync(houseMeter, new DateOnly(2026, 12, 31), 183m);
        await ReadingAsync(meter2, new DateOnly(2026, 6, 14), 33m);
        await ReadingAsync(meter2, new DateOnly(2026, 12, 31), 83m);
        Validate(await WithAsync<IMeterService, Result>(s => s.CreateAsync(
            house,
            new MeterCreation
            {
                Data = new MeterInput { Type = MeterType.ColdWater, Name = "Kaltwasser Wohnung 1 (berechnet)", Number = "RZ-1", AssignmentId = apartment1 },
                Terms = [new MeterTermInput(houseMeter, false), new MeterTermInput(meter2, true)],
            },
            Ct)));

        var result = (await PreviewAsync()).Result;

        // Wohnung 1: 183 − 83 = 100 m³; Wohnung 2: Ben 33 + Clara 50 = 83 m³; 732.00 € ÷ 183 m³ = 4.00 €/m³
        Assert.False(result.HasErrors);
        Assert.Equal([400m, 132m, 200m], new[] { leaseAnna, leaseBen, leaseClara }.Select(l => Share(result, l, "betrkv-02")));
    }

    [Fact]
    public async Task Missing_meter_or_reading_is_reported_as_an_error()
    {
        await StartAccountingAsync();
        await ReceiptAsync("Stadtwerke", "betrkv-02", 732m);
        await SetRuleAsync("betrkv-02", AllocationKey.Consumption, meter: MeterType.ColdWater);
        var meter1 = await MeterAsync(apartment1, "W-1", new DateOnly(2025, 12, 31), 0m);

        var result = (await PreviewAsync()).Result;

        Assert.True(result.HasErrors);
        Assert.Contains(result.Problems, p => p.Text.Contains("Wohnung 2: Der Einheit ist kein Zähler vom Typ „Kaltwasser“ zugeordnet", StringComparison.Ordinal));
        Assert.Contains(result.Problems, p => p.Text.Contains("Wohnung 1: Für Zähler W-1 fehlt ein Stand", StringComparison.Ordinal));
        Assert.NotEqual(Guid.Empty, meter1);
    }

    [Fact]
    public async Task Key_change_from_a_date_applies_to_the_following_part_of_the_year_and_history_stays()
    {
        await StartAccountingAsync();
        await ReceiptAsync("Stadt Köln", "betrkv-08", 4000m);
        await SetRuleAsync("betrkv-08", AllocationKey.LivingArea, new DateOnly(2020, 1, 1));
        await SetRuleAsync("betrkv-08", AllocationKey.Units, new DateOnly(2026, 7, 1));

        var overview = (await WithAsync<ICostAllocation, CostAllocationOverview?>(s => s.GetAsync(house, new DateOnly(2026, 8, 1), Ct)))!;
        var row = overview.Rows.Single(r => r.CostType == "betrkv-08");
        Assert.Equal(AllocationKey.Units, row.Current!.Key);
        Assert.Equal([AllocationKey.Units, AllocationKey.LivingArea], row.History.Select(h => h.Key));
        Assert.Equal(new DateOnly(2026, 6, 30), row.History[1].Validity.To);
        Assert.Equal(AllocationKey.LivingArea, (await WithAsync<ICostAllocation, CostAllocationOverview?>(s => s.GetAsync(house, new DateOnly(2026, 3, 1), Ct)))!.Rows.Single(r => r.CostType == "betrkv-08").Current!.Key);

        var type = (await PreviewAsync()).Result.CostTypes.Single();

        // 4,000.00 € × 181/365 days by area (1,983.56 €) and × 184/365 by units (2,016.44 €)
        Assert.Equal(4000m, type.Total);
        Assert.Equal("Wohnfläche (01.01.2026 – 30.06.2026), Einheiten (01.07.2026 – 31.12.2026)", type.Key);
    }

    [Fact]
    public async Task Setting_a_key_validates_and_the_same_day_replaces_it()
    {
        Assert.False((await SetRuleResultAsync("betrkv-02", AllocationKey.Consumption)).Succeeded); // meter type missing
        Assert.Contains("nicht umlagefähig", (await SetRuleResultAsync("verwaltung", AllocationKey.LivingArea)).Errors.Single(), StringComparison.Ordinal);
        Assert.Contains("Heizkostenverordnung", (await SetRuleResultAsync("betrkv-04", AllocationKey.LivingArea)).Errors.Single(), StringComparison.Ordinal);
        Assert.Contains("höchstens 100 %", (await WithAsync<ICostAllocation, Result>(s => s.SetRuleAsync(house, new CostRuleInput
        {
            CostType = "betrkv-14",
            From = new DateOnly(2026, 1, 1),
            Key = AllocationKey.FixedPercent,
            Shares = [new UnitShareInput { UnitId = apartment1.ToString(), Percent = "70" }, new UnitShareInput { UnitId = apartment2.ToString(), Percent = "40" }],
        }, Ct))).Errors.Single(), StringComparison.Ordinal);

        Validate(await SetRuleResultAsync("betrkv-10", AllocationKey.LivingArea, new DateOnly(2026, 1, 1)));
        Validate(await SetRuleResultAsync("betrkv-10", AllocationKey.Persons, new DateOnly(2026, 1, 1)));
        Assert.Contains("Stichtag muss am oder nach", (await SetRuleResultAsync("betrkv-10", AllocationKey.Units, new DateOnly(2025, 6, 1))).Errors.Single(), StringComparison.Ordinal);

        var row = (await WithAsync<ICostAllocation, CostAllocationOverview?>(s => s.GetAsync(house, new DateOnly(2026, 2, 1), Ct)))!.Rows.Single(r => r.CostType == "betrkv-10");
        Assert.Equal([AllocationKey.Persons], row.History.Select(h => h.Key));

        // End: the cost type is no longer active
        Validate(await WithAsync<ICostAllocation, Result>(s => s.EndRuleAsync(house, "betrkv-10", new DateOnly(2026, 6, 30), Ct)));
        Assert.Null((await WithAsync<ICostAllocation, CostAllocationOverview?>(s => s.GetAsync(house, new DateOnly(2026, 7, 1), Ct)))!.Rows.Single(r => r.CostType == "betrkv-10").Current);
        Assert.False((await WithAsync<ICostAllocation, Result>(s => s.EndRuleAsync(house, "betrkv-10", new DateOnly(2026, 12, 1), Ct))).Succeeded);
    }

    [Fact]
    public async Task Fixed_percent_and_pre_deduction_are_stored_and_calculated()
    {
        await StartAccountingAsync();
        await ReceiptAsync("Hausmeister", "betrkv-14", 1000m);
        Validate(await WithAsync<ICostAllocation, Result>(s => s.SetRuleAsync(house, new CostRuleInput
        {
            CostType = "betrkv-14",
            From = new DateOnly(2020, 1, 1),
            Key = AllocationKey.FixedPercent,
            PreDeductionPercent = 10m,
            PreDeductionNote = "Anteil Garten",
            ExcludeVacantRooms = true,
            Shares = [new UnitShareInput { UnitId = apartment1.ToString(), Percent = "50" }, new UnitShareInput { UnitId = apartment2.ToString(), Percent = "50" }],
        }, Ct)));

        var row = (await WithAsync<ICostAllocation, CostAllocationOverview?>(s => s.GetAsync(house, new DateOnly(2026, 1, 1), Ct)))!.Rows.Single(r => r.CostType == "betrkv-14").Current!;
        Assert.Equal((10m, "Anteil Garten", true), (row.PreDeductionPercent, row.PreDeductionNote, row.ExcludeVacantRooms));
        Assert.Equal([("Wohnung 1", 50m), ("Wohnung 2", 50m)], row.Shares.Select(a => (a.UnitName, a.Percent)));

        var result = (await PreviewAsync()).Result;

        // 10 % pre-deduction = 100.00 €; 900.00 € × 50 % for each apartment: Anna 450.00; Wohnung 2: Ben 165/365 × 450 = 203.42, Clara 246.58
        Assert.Equal(450m, Share(result, leaseAnna, "betrkv-14"));
        Assert.Equal(203.42m, Share(result, leaseBen, "betrkv-14"));
        Assert.Equal(246.58m, Share(result, leaseClara, "betrkv-14"));
        Assert.Equal(100m, result.Landlord.Single(p => p.Reason == LandlordReason.PreDeduction).Amount);
    }

    [Fact]
    public async Task Cost_type_not_agreed_in_the_lease_is_charged_to_the_landlord()
    {
        await StartAccountingAsync();
        await ReceiptAsync("Gärtner", "betrkv-10", 1000m);
        await SetRuleAsync("betrkv-10", AllocationKey.LivingArea);

        var before = (await WithAsync<ICostAllocation, LeaseCostAgreement?>(s => s.GetLeaseAgreementAsync(leaseAnna, Ct)))!;
        Assert.All(before.Rows, r => Assert.True(r.Agreed));
        Assert.Contains("Anna Schmidt", before.Description, StringComparison.Ordinal);
        Assert.Equal(house, before.PropertyId);
        Validate(await WithAsync<ICostAllocation, Result>(s => s.SetLeaseAgreementAsync(leaseAnna, ["betrkv-10"], Ct)));
        Assert.False((await WithAsync<ICostAllocation, Result>(s => s.SetLeaseAgreementAsync(leaseAnna, ["instandhaltung"], Ct))).Succeeded);

        var result = (await PreviewAsync()).Result;

        var line = result.Tenants.Single(t => t.LeaseId == leaseAnna).Lines.Single();
        Assert.Equal((0m, 600m), (line.Share, line.NotAgreed));
        Assert.Equal(600m, result.Landlord.Single(p => p.Reason == LandlordReason.NotAgreed).Amount);
        Assert.False((await WithAsync<ICostAllocation, LeaseCostAgreement?>(s => s.GetLeaseAgreementAsync(leaseAnna, Ct)))!.Rows.Single(r => r.CostType == "betrkv-10").Agreed);
    }

    [Fact]
    public async Task Shared_flat_is_excluded_as_flat_rate_or_distributed_over_the_rooms()
    {
        var flat = await SharedFlatAsync();
        await StartAccountingAsync();
        await ReceiptAsync("Stadt Köln", "betrkv-01", 1000m);
        await SetRuleAsync("betrkv-01", AllocationKey.LivingArea);

        // Flat rate (default): the rooms get no statement, but are compared internally.
        var flatRate = (await PreviewAsync()).Result;
        Assert.DoesNotContain(flatRate.Tenants, t => t.Rental.StartsWith("WG", StringComparison.Ordinal));
        Assert.Equal(2, flatRate.FlatRates.Count);
        Assert.True(flatRate.FlatRates.All(f => f.ActualCosts > 0));
        Assert.Equal(1000m, flatRate.TenantTotal + flatRate.LandlordTotal);

        Validate(await WithAsync<ICostAllocation, Result>(s => s.SetSharedFlatBillingAsync(flat, SharedFlatBilling.ByRoom, Ct)));
        Assert.False((await WithAsync<ICostAllocation, Result>(s => s.SetSharedFlatBillingAsync(apartment1, SharedFlatBilling.ByRoom, Ct))).Succeeded);
        Assert.Equal(SharedFlatBilling.ByRoom, (await WithAsync<ICostAllocation, CostAllocationOverview?>(s => s.GetAsync(house, new DateOnly(2026, 1, 1), Ct)))!.SharedFlats.Single().Mode);

        var byRoom = (await PreviewAsync()).Result;

        // Areas: Wohnung 1 60 m², Wohnung 2 40 m² (Ben/Clara), WG 50 m²; rooms 20 + 5 and 20 + 5 = 25 m² each
        Assert.Empty(byRoom.FlatRates);
        var rooms = byRoom.Tenants.Where(t => t.Rental.StartsWith("WG", StringComparison.Ordinal)).ToList();
        Assert.Equal(["WG, Zimmer 1", "WG, Zimmer 2"], rooms.OrderBy(t => t.Rental, StringComparer.Ordinal).Select(t => t.Rental));
        Assert.Equal(1000m, byRoom.TenantTotal + byRoom.LandlordTotal);
    }

    [Fact]
    public async Task Subtype_refines_the_item_and_removing_it_keeps_the_cost_type()
    {
        var added = await WithAsync<ICostSubtypes, Result>(s => s.AddAsync("betrkv-13", "Gebäudeversicherung", Ct));
        var id = Validate(added);
        Assert.Contains("gibt es schon", (await WithAsync<ICostSubtypes, Result>(s => s.AddAsync("betrkv-13", "gebäudeversicherung", Ct))).Errors.Single(), StringComparison.Ordinal);
        Assert.Contains("Unbekannte Kostenart", (await WithAsync<ICostSubtypes, Result>(s => s.AddAsync("gibt-es-nicht", "X", Ct))).Errors.Single(), StringComparison.Ordinal);

        var receipt = Validate(await WithAsync<IReceiptService, Result>(s => s.CreateAsync(
            new ReceiptInput { SupplierName = "Versicherung AG", InvoiceNumber = "V-1", InvoiceDate = new DateOnly(2026, 1, 15), Gross = 800m },
            [],
            new CostItemInput { CostType = "betrkv-13", PropertyId = house, SubtypeId = id, From = new DateOnly(2026, 1, 1), To = new DateOnly(2026, 12, 31) },
            Ct)));
        var wrongType = await WithAsync<IReceiptService, Result>(s => s.AddItemAsync(receipt, new CostItemInput { CostType = "betrkv-10", PropertyId = house, SubtypeId = id, Amount = 0.01m }, Ct));
        Assert.Contains("gehört nicht zur gewählten Kostenart", wrongType.Errors.Single(), StringComparison.Ordinal);
        Assert.Equal("Gebäudeversicherung", (await WithAsync<IReceiptService, ReceiptDetails?>(s => s.GetAsync(receipt, Ct)))!.Items.Single().Subtype);
        Assert.Equal(id, (await WithAsync<IReceiptService, ReceiptOptions>(s => s.OptionsAsync(Ct))).Subtypes.Single().Id);

        var result = (await PreviewAsync()).Result;
        Assert.Equal("Gebäudeversicherung: Versicherung AG, Rechnung V-1", result.CostTypes.Single().Items.Single().Description);

        Validate(await WithAsync<ICostSubtypes, Result>(s => s.RemoveAsync(id, Ct)));
        var item = (await WithAsync<IReceiptService, ReceiptDetails?>(s => s.GetAsync(receipt, Ct)))!.Items.Single();
        Assert.Equal(("betrkv-13", null), (item.CostType, item.Subtype));
        Assert.Empty(await WithAsync<ICostSubtypes, IReadOnlyList<CostSubtypeView>>(s => s.ListAsync(Ct)));
    }

    [Fact]
    public async Task Hint_when_accounting_has_not_started_and_unknown_property_gives_null()
    {
        var preview = await PreviewAsync();

        Assert.Contains(preview.Hints, h => h.Contains("kein Buchhaltungsbeginn", StringComparison.Ordinal));
        Assert.Null(await WithAsync<IOperatingCosts, OperatingCostPreview?>(s => s.PreviewAsync(Guid.NewGuid(), 2026, Ct)));
        Assert.Null(await WithAsync<ICostAllocation, CostAllocationOverview?>(s => s.GetAsync(Guid.NewGuid(), new DateOnly(2026, 1, 1), Ct)));
        await Assert.ThrowsAsync<RuleViolationException>(() => WithAsync<IOperatingCosts, OperatingCostPreview?>(s => s.PreviewAsync(house, new Period(new DateOnly(2026, 1, 1), new DateOnly(2027, 3, 1)), Ct)));
    }

    [Fact]
    public async Task Supply_contract_replaces_advance_receipts_with_consumption_times_tariff_and_a_supplier_statement_wins()
    {
        await StartAccountingAsync();
        await SetRuleAsync("betrkv-02", AllocationKey.LivingArea);
        var advance = await ReceiptAsync("Stadtwerke", "betrkv-02", 500m);
        var main = Validate(await WithAsync<IMeterService, Result>(s => s.CreateAsync(
            house,
            new MeterCreation { Data = new MeterInput { Type = MeterType.ColdWater, Name = "Hauptzähler", Number = "H-1" }, InstalledOn = new DateOnly(2025, 12, 31), InitialReading = 1000m },
            Ct)));
        await ReadingAsync(main, new DateOnly(2026, 12, 31), 1183m);
        Validate(await WithAsync<ISupplyContracts, Result>(s => s.CreateAsync(
            house,
            new SupplyContractInput
            {
                Name = "Wasser",
                CostType = "betrkv-02",
                MeterType = MeterType.ColdWater,
                MeterIds = [main],
                Tariff = new SupplyTariffInput { From = new DateOnly(2020, 1, 1), UnitPrice = 4m, BaseFeePerMonth = 12m },
            },
            Ct)));

        var derived = await PreviewAsync();

        // 183 m³ × 4.00 € + 12 × 12.00 € = 876.00 €; the advance receipt (500.00 €) is not a cost.
        Assert.Equal(876m, derived.Result.CostTypes.Single(t => t.CostType == "betrkv-02").Total);
        Assert.Contains(derived.Hints, h => h.Contains("nicht angesetzt", StringComparison.Ordinal) && h.Contains("876,00", StringComparison.Ordinal));
        Assert.Contains(derived.Hints, h => h.Contains("voraussichtlich eine Nachzahlung von 376,00", StringComparison.Ordinal));
        Assert.Contains(derived.Result.CostTypes.Single(t => t.CostType == "betrkv-02").Items.Select(i => i.Description), d => d.Contains("183 m³ × 4 €", StringComparison.Ordinal));
        Assert.NotEqual(Guid.Empty, advance);

        // The annual statement of the supplier replaces the calculation (and the advance receipt stays out).
        Validate(await WithAsync<IReceiptService, Result>(s => s.CreateAsync(
            new ReceiptInput { SupplierName = "Stadtwerke", InvoiceDate = new DateOnly(2027, 5, 10), Gross = 900m, SupplierStatement = true },
            [],
            new CostItemInput { CostType = "betrkv-02", PropertyId = house, From = new DateOnly(2026, 1, 1), To = new DateOnly(2026, 12, 31) },
            Ct)));

        var actual = await PreviewAsync();

        Assert.Equal(900m, actual.Result.CostTypes.Single(t => t.CostType == "betrkv-02").Total);
        Assert.Contains(actual.Hints, h => h.Contains("Jahresabrechnung des Versorgers", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Gas_supply_contract_converts_cubic_meters_by_Brennwert_and_splits_the_CO2_costs_off_the_fuel_costs()
    {
        await StartAccountingAsync();
        Validate(await WithAsync<IHeatingSettings, Result>(s => s.SaveAsync(house, new HeatingSettingsInput
        {
            Fuel = HeatingFuel.Gas,
            ConsumptionPercent = 70m,
            JointPlant = true,
            ApplyCarbonCosts = true,
            FuelKwhPerUnit = 10m,
            EnergyYears = [new FuelEnergyYearInput { Year = 2026, CalorificValue = 11m, StateNumber = 0.9m, CarbonPricePerTon = 55m }],
        }, Ct)));
        var gas = Validate(await WithAsync<IMeterService, Result>(s => s.CreateAsync(
            house,
            new MeterCreation { Data = new MeterInput { Type = MeterType.Gas, Name = "Gaszähler", Number = "G-1" }, InstalledOn = new DateOnly(2025, 12, 31), InitialReading = 1000m },
            Ct)));
        await ReadingAsync(gas, new DateOnly(2026, 12, 31), 3000m);
        Validate(await WithAsync<ISupplyContracts, Result>(s => s.CreateAsync(
            house,
            new SupplyContractInput
            {
                Name = "Gas",
                CostType = "betrkv-06",
                MeterType = MeterType.Gas,
                PriceUnit = PriceUnit.KilowattHour,
                HeatingKind = HeatingCostKind.Fuel,
                MeterIds = [gas],
                Tariff = new SupplyTariffInput { From = new DateOnly(2020, 1, 1), UnitPrice = 0.1m },
            },
            Ct)));

        var preview = await PreviewAsync();

        // 2,000 m³ × 11 × 0.9 = 19,800 kWh × 0.10 € = 1,980.00 €; CO₂ 19,800 × 0.2016 = 3,991.7 kg × 55 €/t = 219.54 € of it
        var items = preview.Result.CostTypes.SelectMany(t => t.Items).ToList();
        Assert.Equal(1980m, items.Sum(i => i.Prorated));
        Assert.Equal(219.54m, items.Single(i => i.Description.StartsWith("CO₂-Kosten", StringComparison.Ordinal)).Prorated);
    }

    [Fact]
    public async Task Supply_contract_reports_a_deviation_between_main_meter_and_unit_meters_and_can_leave_its_share_with_the_landlord()
    {
        await StartAccountingAsync();
        await SetRuleAsync("betrkv-02", AllocationKey.Consumption, meter: MeterType.ColdWater);
        var houseMeter = await MeterAsync(null, "H-1", new DateOnly(2025, 12, 31), 0m);
        var meter1 = await MeterAsync(apartment1, "W-1", new DateOnly(2025, 12, 31), 0m);
        var meter2 = await MeterAsync(apartment2, "W-2", new DateOnly(2025, 12, 31), 0m);
        await ReadingAsync(houseMeter, new DateOnly(2026, 12, 31), 183m);
        await ReadingAsync(meter1, new DateOnly(2026, 12, 31), 100m);
        await ReadingAsync(meter2, new DateOnly(2026, 12, 31), 50m);

        async Task CreateContractAsync(DeviationHandling deviation) =>
            Validate(await WithAsync<ISupplyContracts, Result>(s => s.CreateAsync(
                house,
                new SupplyContractInput
                {
                    Name = "Wasser",
                    CostType = "betrkv-02",
                    MeterType = MeterType.ColdWater,
                    MeterIds = [houseMeter],
                    Deviation = deviation,
                    Tariff = new SupplyTariffInput { From = new DateOnly(2020, 1, 1), UnitPrice = 4m, BaseFeePerMonth = 12m },
                },
                Ct)));

        await CreateContractAsync(DeviationHandling.DistributeToUnits);
        var distributed = await PreviewAsync();

        // 183 m³ × 4.00 € + 12 × 12.00 € = 876.00 €, all allocated over the unit meters; the check reports 33 m³ (18 %).
        Assert.Equal(876m, distributed.Result.CostTypes.Single(t => t.CostType == "betrkv-02").Total);
        Assert.Contains(distributed.Hints, h => h.StartsWith("Plausibilität:", StringComparison.Ordinal) && h.Contains("18", StringComparison.Ordinal));
        Assert.Empty(distributed.Result.NonAllocable);
        var check = Assert.Single(distributed.MeterChecks!);
        Assert.Equal((183m, 150m, 18.0m, CheckLevel.Warning), (check.Main, check.UnitsTotal, check.DifferencePercent, check.Level));
        Assert.Equal([100m, 50m], check.Consumption.Select(c => c.Quantity));

        // The landlord bears the difference: tenants pay 150/183 of the cost (718.03 €), 157.97 € stay with the landlord.
        var existing = await WithAsync<ISupplyContracts, SupplyOverview>(async s => (await s.GetAsync(house, Ct))!);
        Validate(await WithAsync<ISupplyContracts, Result>(s => s.RemoveAsync(existing.Contracts.Single().Id, Ct)));
        await CreateContractAsync(DeviationHandling.Landlord);
        var landlord = await PreviewAsync();

        Assert.Equal(718.03m, landlord.Result.CostTypes.Single(t => t.CostType == "betrkv-02").Total);
        Assert.Equal(157.97m, Assert.Single(landlord.Result.NonAllocable).Amount);
    }

    [Fact]
    public async Task Deviation_check_of_the_cold_water_main_meter_includes_the_hot_water_meters_of_the_units()
    {
        await StartAccountingAsync();
        await SetRuleAsync("betrkv-02", AllocationKey.Consumption, meter: MeterType.ColdWater);
        var houseMeter = await MeterAsync(null, "H-1", new DateOnly(2025, 12, 31), 0m);
        var cold1 = await MeterAsync(apartment1, "W-1", new DateOnly(2025, 12, 31), 0m);
        var cold2 = await MeterAsync(apartment2, "W-2", new DateOnly(2025, 12, 31), 0m);
        var hot1 = await MeterAsync(apartment1, "WW-1", new DateOnly(2025, 12, 31), 0m, MeterType.HotWater);
        await ReadingAsync(houseMeter, new DateOnly(2026, 12, 31), 183m);
        await ReadingAsync(cold1, new DateOnly(2026, 12, 31), 100m);
        await ReadingAsync(cold2, new DateOnly(2026, 12, 31), 50m);
        await ReadingAsync(hot1, new DateOnly(2026, 12, 31), 33m);
        Validate(await WithAsync<ISupplyContracts, Result>(s => s.CreateAsync(
            house,
            new SupplyContractInput
            {
                Name = "Wasser",
                CostType = "betrkv-02",
                MeterType = MeterType.ColdWater,
                MeterIds = [houseMeter],
                Tariff = new SupplyTariffInput { From = new DateOnly(2020, 1, 1), UnitPrice = 4m },
            },
            Ct)));

        var preview = await PreviewAsync();

        // 100 + 50 cold + 33 hot = 183 m³ = main meter: no deviation
        var check = Assert.Single(preview.MeterChecks!);
        Assert.Equal((183m, 183m, CheckLevel.Ok), (check.Main, check.UnitsTotal, check.Level));
        Assert.DoesNotContain(preview.Hints, h => h.StartsWith("Plausibilität:", StringComparison.Ordinal));

        // Hot water with its own main meter: only the cold water meters are compared (150 of 183 m³)
        var existing = await WithAsync<ISupplyContracts, SupplyOverview>(async s => (await s.GetAsync(house, Ct))!);
        var contract = existing.Contracts.Single();
        Validate(await WithAsync<ISupplyContracts, Result>(s => s.UpdateAsync(
            contract.Id,
            new SupplyContractInput
            {
                Name = contract.Name,
                CostType = contract.CostType,
                MeterType = contract.MeterType,
                MeterIds = [houseMeter],
                UnitMeters = UnitMeterScope.SameType,
            },
            Ct)));

        var sameType = Assert.Single((await PreviewAsync()).MeterChecks!);
        Assert.Equal((183m, 150m, CheckLevel.Warning), (sameType.Main, sameType.UnitsTotal, sameType.Level));
    }

    [Fact]
    public async Task Deviation_check_counts_a_calculated_unit_meter_once_and_not_the_combined_meter_behind_it()
    {
        await StartAccountingAsync();
        await SetRuleAsync("betrkv-02", AllocationKey.Consumption, meter: MeterType.ColdWater);
        var houseMeter = await MeterAsync(null, "H-1", new DateOnly(2025, 12, 31), 0m);
        // One meter counts flat 1 and flat 2 together; flat 1 = combined meter − flat 2
        var combined = await MeterAsync(null, "K-12", new DateOnly(2025, 12, 31), 0m);
        var meter2 = await MeterAsync(apartment2, "W-2", new DateOnly(2025, 12, 31), 0m);
        var hot2 = await MeterAsync(apartment2, "WW-2", new DateOnly(2025, 12, 31), 0m, MeterType.HotWater);
        foreach (var (meter, value) in new[] { (houseMeter, 100m), (combined, 60m), (meter2, 15m), (hot2, 8m) })
        {
            await ReadingAsync(meter, new DateOnly(2026, 12, 31), value);
        }

        Validate(await WithAsync<IMeterService, Result>(s => s.CreateAsync(
            house,
            new MeterCreation
            {
                Data = new MeterInput { Type = MeterType.ColdWater, Name = "Kaltwasser Wohnung 1 (berechnet)", Number = "RZ-1", AssignmentId = apartment1 },
                Terms = [new MeterTermInput(combined, false), new MeterTermInput(meter2, true)],
            },
            Ct)));
        Validate(await WithAsync<ISupplyContracts, Result>(s => s.CreateAsync(
            house,
            new SupplyContractInput
            {
                Name = "Wasser",
                CostType = "betrkv-02",
                MeterType = MeterType.ColdWater,
                MeterIds = [houseMeter],
                Tariff = new SupplyTariffInput { From = new DateOnly(2020, 1, 1), UnitPrice = 4m },
            },
            Ct)));

        var check = Assert.Single((await PreviewAsync()).MeterChecks!);

        // flat 1: 60 − 15 = 45, flat 2: 15 cold + 8 hot → 68 m³ against 100 m³ at the main meter
        Assert.Equal((100m, 68m), (check.Main, check.UnitsTotal));
    }

    [Fact]
    public async Task A_new_supply_contract_keeps_the_unit_meter_scope_same_type_although_the_database_default_is_different()
    {
        await StartAccountingAsync();
        var houseMeter = await MeterAsync(null, "H-1", new DateOnly(2025, 12, 31), 0m);

        Validate(await WithAsync<ISupplyContracts, Result>(s => s.CreateAsync(
            house,
            new SupplyContractInput
            {
                Name = "Wasser",
                CostType = "betrkv-02",
                MeterType = MeterType.ColdWater,
                MeterIds = [houseMeter],
                UnitMeters = UnitMeterScope.SameType,
                Tariff = new SupplyTariffInput { From = new DateOnly(2020, 1, 1), UnitPrice = 4m },
            },
            Ct)));

        var overview = await WithAsync<ISupplyContracts, SupplyOverview>(async s => (await s.GetAsync(house, Ct))!);
        Assert.Equal(UnitMeterScope.SameType, overview.Contracts.Single().UnitMeters);
    }

    [Fact]
    public async Task Supply_contract_without_readings_falls_back_to_the_receipts_with_a_hint()
    {
        await StartAccountingAsync();
        await SetRuleAsync("betrkv-02", AllocationKey.LivingArea);
        await ReceiptAsync("Stadtwerke", "betrkv-02", 500m);
        var main = Validate(await WithAsync<IMeterService, Result>(s => s.CreateAsync(
            house,
            new MeterCreation { Data = new MeterInput { Type = MeterType.ColdWater, Name = "Hauptzähler", Number = "H-1" }, InstalledOn = new DateOnly(2025, 12, 31), InitialReading = 1000m },
            Ct)));
        Validate(await WithAsync<ISupplyContracts, Result>(s => s.CreateAsync(
            house,
            new SupplyContractInput
            {
                Name = "Wasser",
                CostType = "betrkv-02",
                MeterType = MeterType.ColdWater,
                MeterIds = [main],
                Tariff = new SupplyTariffInput { From = new DateOnly(2020, 1, 1), UnitPrice = 4m },
            },
            Ct)));

        var preview = await PreviewAsync();

        Assert.Equal(500m, preview.Result.CostTypes.Single(t => t.CostType == "betrkv-02").Total);
        Assert.Contains(preview.Hints, h => h.StartsWith("Fehlt:", StringComparison.Ordinal) && h.Contains("Es gelten die Belege", StringComparison.Ordinal));
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

    private Task<Result> SetRuleResultAsync(string type, AllocationKey key, DateOnly? from = null, MeterType? meter = null) =>
        WithAsync<ICostAllocation, Result>(s => s.SetRuleAsync(house, new CostRuleInput { CostType = type, From = from ?? new DateOnly(2020, 1, 1), Key = key, MeterType = meter }, Ct));

    private async Task SetRuleAsync(string type, AllocationKey key, DateOnly? from = null, MeterType? meter = null) =>
        Validate(await SetRuleResultAsync(type, key, from, meter));

    private async Task<Guid> ReceiptAsync(string supplier, string costType, decimal amount) =>
        Validate(await WithAsync<IReceiptService, Result>(s => s.CreateAsync(
            new ReceiptInput { SupplierName = supplier, InvoiceDate = new DateOnly(2026, 12, 15), Gross = amount },
            [],
            new CostItemInput { CostType = costType, PropertyId = house, From = new DateOnly(2026, 1, 1), To = new DateOnly(2026, 12, 31) },
            Ct)));

    private async Task StartAccountingAsync()
    {
        await WithAsync<IFinanceSettings, bool>(async s =>
        {
            await s.SaveAsync(new FinanceSettingsInput { AccountingStart = new DateOnly(2026, 1, 1) }, Ct);
            return true;
        });
        await WithAsync<ITenantAccounts, int>(s => s.GenerateChargesAsync(null, Ct));
    }

    /// <summary>
    /// Anna pays every month (rent and prepayment) except December, Ben pays everything, Clara pays nothing.
    /// </summary>
    private async Task PayAsync()
    {
        for (var month = 1; month <= 11; month++)
        {
            var date = new DateOnly(2026, month, 5);
            Validate(await WithAsync<ITenantAccounts, Result>(s => s.RecordPaymentAsync(leaseAnna, new PaymentInput { Date = date, Amount = 600m }, Ct)));
        }

        // Ben: January–May 550.00 € each, June (14/30): 233.33 + 23.33 = 256.66 €
        Validate(await WithAsync<ITenantAccounts, Result>(s => s.RecordPaymentAsync(leaseBen, new PaymentInput { Date = new DateOnly(2026, 7, 1), Amount = 5 * 550m + 256.66m }, Ct)));
    }

    private async Task<Guid> MeterAsync(Guid? unit, string number, DateOnly installed, decimal initial, MeterType type = MeterType.ColdWater) =>
        Validate(await WithAsync<IMeterService, Result>(s => s.CreateAsync(
            house,
            new MeterCreation { Data = new MeterInput { Type = type, Name = type == MeterType.HotWater ? "Warmwasser" : "Kaltwasser", Number = number, AssignmentId = unit }, InstalledOn = installed, InitialReading = initial },
            Ct)));

    private async Task ReadingAsync(Guid meter, DateOnly date, decimal value)
    {
        var result = await WithAsync<IMeterService, ReadingResult>(s => s.RecordReadingAsync(meter, new ReadingInput { Date = date, Value = value, Confirmed = true }, null, Ct));
        Assert.True(result.Succeeded, string.Join(", ", result.Errors));
    }

    /// <summary>WG "WG" (50 m²) with two rooms (20 m² + 5 m² share of common area each), both rented from 2025.</summary>
    private async Task<Guid> SharedFlatAsync()
    {
        await using var scope = environment.Services.CreateAsyncScope();
        var properties = scope.ServiceProvider.GetRequiredService<IPropertyService>();
        var flat = Validate(await properties.CreateUnitAsync(house, new UnitInput { Name = "WG" }, Ct));
        Validate(await properties.SetUnitAreaAsync(flat, new AreaInput { From = new DateOnly(2020, 1, 1), Area = 50m }, Ct));
        var room1 = Validate(await properties.CreateRoomAsync(flat, new RoomInput { Name = "Zimmer 1" }, Ct));
        var room2 = Validate(await properties.CreateRoomAsync(flat, new RoomInput { Name = "Zimmer 2" }, Ct));
        Validate(await properties.SetRoomAreaAsync(room1, new AreaInput { From = new DateOnly(2020, 1, 1), Area = 20m, SecondaryArea = 5m }, Ct));
        Validate(await properties.SetRoomAreaAsync(room2, new AreaInput { From = new DateOnly(2020, 1, 1), Area = 20m, SecondaryArea = 5m }, Ct));
        var people = scope.ServiceProvider.GetRequiredService<IPersonService>();
        var leases = scope.ServiceProvider.GetRequiredService<ILeaseService>();
        var mia = Validate(await people.CreateAsync(new PersonInput { FirstName = "Mia", LastName = "Kern" }, null, Ct));
        var noah = Validate(await people.CreateAsync(new PersonInput { FirstName = "Noah", LastName = "Berg" }, null, Ct));
        Validate(await leases.CreateAsync(new LeaseCreation { RentalUnitId = room1, Start = new DateOnly(2025, 1, 1), TenantIds = [mia], BaseRent = 400m, OperatingCostPrepayment = 40m }, Ct));
        Validate(await leases.CreateAsync(new LeaseCreation { RentalUnitId = room2, Start = new DateOnly(2025, 1, 1), TenantIds = [noah], BaseRent = 400m, OperatingCostPrepayment = 40m }, Ct));
        return flat;
    }
}
