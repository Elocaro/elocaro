using MietHahn.Domain.Receipts;
using MietHahn.Domain.Common;
using MietHahn.Domain.Heating;
using MietHahn.Domain.Leases;
using MietHahn.Domain.Metering;
using MietHahn.Domain.OperatingCosts;
using static MietHahn.Domain.Tests.Heating.HeatingScenario;
using static MietHahn.Domain.Tests.OperatingCosts.Scenario;

namespace MietHahn.Domain.Tests.Heating;

/// <summary>Oil from the tank stock, flat rate warning (§ 12), vacancy, shared flat, settings and random checks.</summary>
public class HeatingRulesTests
{
    private static readonly HeatingParameters Plain = new() { JointPlant = false, ApplyCarbonCosts = false };

    private static RuleConsumption Totals(params (int Unit, MeterType Type, decimal Value)[] values) => RuleConsumption.Totals(Year2026, values);

    // ---- oil -------------------------------------------------------------------------------------------------------

    private static readonly OilDelivery[] Deliveries =
    [
        new(D(2026, 3, 15), 2000m, 1900m),
        new(D(2026, 9, 20), 1500m, 1500m),
    ];

    [Fact]
    public void Oil_consumption_is_valued_with_the_weighted_average()
    {
        // 1,000 l (0.90 €/l = 900.00 €) + 3,500 l delivered (3,400.00 €) − 1,200 l closing = 3,300 l consumed.
        // Average price (900 + 1,900 + 1,500) ÷ (1,000 + 2,000 + 1,500) = 4,300 ÷ 4,500 = 0.95556 €/l → 3,300 × 4,300 ÷ 4,500 = 3,153.33 €
        var result = OilConsumptionCalculator.Calculate(1000m, 1200m, 0.90m, Deliveries, OilValuation.WeightedAverage);

        Assert.Equal(3300m, result.Liters);
        Assert.Equal(3153.33m, decimal.Round(result.Cost, 2));
    }

    [Fact]
    public void Oil_consumption_is_valued_first_in_first_out()
    {
        // 1,000 l × 0.90 = 900.00 + 2,000 l × 0.95 = 1,900.00 + 300 l × 1.00 = 300.00 → 3,100.00 €
        var result = OilConsumptionCalculator.Calculate(1000m, 1200m, 0.90m, Deliveries, OilValuation.Fifo);

        Assert.Equal(3300m, result.Liters);
        Assert.Equal(3100.00m, result.Cost);
    }

    [Fact]
    public void Oil_closing_stock_above_the_available_quantity_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => OilConsumptionCalculator.Calculate(100m, 5000m, 0.9m, Deliveries, OilValuation.Fifo));
    }

    private static BillingInput OilHouse(IReadOnlyList<BillingCost>? extra = null)
    {
        var costs = new List<BillingCost>
        {
            HeatCost(101, "betrkv-04", 1900m, HeatingCostKind.Fuel, new Period(D(2026, 3, 15), D(2026, 3, 15)), receipt: 1, quantity: 2000m, unit: "l"),
            HeatCost(102, "betrkv-04", 1500m, HeatingCostKind.Fuel, new Period(D(2026, 9, 20), D(2026, 9, 20)), receipt: 2, quantity: 1500m, unit: "l"),
            HeatCost(103, "betrkv-04", 200m, HeatingCostKind.Maintenance),
        };
        costs.AddRange(extra ?? []);
        return Input([Unit(1, "Wohnung 1", 100m)], [Lease(11, "A", 1, D(2024, 1, 1))], costs, [], Totals((1, MeterType.Heat, 5000m)));
    }

    [Fact]
    public void Oil_costs_are_the_valued_tank_consumption_instead_of_the_purchases()
    {
        var parameters = Plain with
        {
            Fuel = HeatingFuel.Oil,
            Oil = new OilStock(1000m, 1200m),
            OilOpeningPricePerLiter = 0.90m,
            OilValuation = OilValuation.Fifo,
        };

        var result = Run(OilHouse(), parameters);

        // Fuel costs 3,100.00 € instead of the purchases 3,400.00 €, plus maintenance 200.00 € → 3,300.00 €; 3,300 l × 10 kWh/l = 33,000 kWh
        Assert.Equal(3300.00m, result.CostTypes.Single(t => t.CostType == HeatingCostCalculator.HeatKey).Total);
        var energy = result.Sections.Single(s => s.Id == "heating").Entries.Single(e => e.Title == "Brennstoffenergie");
        Assert.Equal(33000m, energy.Result);
        Assert.False(result.HasErrors);
    }

    [Fact]
    public void Missing_tank_stock_is_an_error_and_the_deliveries_are_used()
    {
        var result = Run(OilHouse(), Plain with { Fuel = HeatingFuel.Oil, Oil = new OilStock(null, 1200m, "Tankstand zu Jahresbeginn fehlt.") });

        Assert.Contains(result.Problems, p => p.Severity == ProblemSeverity.Error && p.Text.Contains("Tankstand zu Jahresbeginn fehlt", StringComparison.Ordinal));
        Assert.Equal(3600.00m, result.CostTypes.Single(t => t.CostType == HeatingCostCalculator.HeatKey).Total);
    }

    // ---- flat rate and vacancy --------------------------------------------------------------------------------------

    private static BillingInput TwoApartments(bool flatRateLease, bool vacantSecond = false)
    {
        var leases = new List<BillingLease> { Lease(11, "A", 1, D(2024, 1, 1), statement: !flatRateLease) };
        if (!vacantSecond)
        {
            leases.Add(Lease(12, "B", 2, D(2024, 1, 1)));
        }

        return Input(
            [Unit(1, "Wohnung 1", 50m), Unit(2, "Wohnung 2", 50m)],
            leases,
            [HeatCost(101, "betrkv-04", 1000m, HeatingCostKind.Other)],
            [],
            Totals((1, MeterType.Heat, 3000m), (2, MeterType.Heat, 1000m)));
    }

    [Fact]
    public void Flat_rate_lease_is_not_billed_by_consumption_and_the_tenant_may_cut_15_percent()
    {
        var result = Run(TwoApartments(flatRateLease: true), Plain);

        // 70 % = 700.00 € by kWh 3,000 : 1,000 → A 525.00, B 175.00; 30 % = 300.00 € by area 50 : 50 → 150.00 each → A 675.00, B 325.00
        Assert.DoesNotContain(result.Tenants, t => t.LeaseId == Id(11));
        Assert.Equal(325.00m, HeatingLine(result, 12, HeatingCostCalculator.HeatKey).Share);
        var landlord = Assert.Single(result.CostTypes.Single().Landlord);
        Assert.Equal((LandlordReason.FlatRate, 675.00m), (landlord.Reason, landlord.Amount));
        Assert.Equal(675.00m, result.FlatRates.Single().ActualCosts);

        // § 12 para. 1: reduction right of 15 % → 15 % × 675.00 € = 101.25 €
        var warning = Assert.Single(result.Problems, p => p.Text.Contains("§ 12", StringComparison.Ordinal));
        Assert.Equal(ProblemSeverity.Warning, warning.Severity);
        Assert.Contains("101,25 €", warning.Text, StringComparison.Ordinal);
        Assert.Contains("15 %", warning.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Flat_rate_lease_with_consumption_based_heating_gets_a_statement_for_the_heating_only()
    {
        var input = TwoApartments(flatRateLease: true);
        var leases = input.Leases.Select(l => l.Id == Id(11) ? l with { ReceivesHeatingStatement = true } : l).ToList();

        var result = Run(input with { Leases = leases }, Plain);

        // A is billed for heating by consumption (675.00 €) although the operating costs are a flat rate: no § 12 warning, no operating lines.
        var statement = result.Tenants.Single(t => t.LeaseId == Id(11));
        Assert.Equal(675.00m, HeatingLine(result, 11, HeatingCostCalculator.HeatKey).Share);
        Assert.All(statement.Lines, l => Assert.Equal(CostGroup.Heating, l.Group));
        Assert.Equal(CostGroup.Heating, Assert.Single(statement.Groups).Group);
        Assert.Equal(675.00m, statement.Balance);
        Assert.Equal(Id(11), Assert.Single(result.FlatRates).LeaseId);
        Assert.DoesNotContain(result.Problems, p => p.Text.Contains("§ 12", StringComparison.Ordinal));
    }

    [Fact]
    public void Without_flat_rate_there_is_no_reduction_warning()
    {
        Assert.DoesNotContain(Run(TwoApartments(flatRateLease: false), Plain).Problems, p => p.Text.Contains("§ 12", StringComparison.Ordinal));
    }

    [Fact]
    public void Vacancy_stays_with_the_landlord_including_the_consumption_of_the_vacant_unit()
    {
        var result = Run(TwoApartments(flatRateLease: false, vacantSecond: true), Plain);

        // Wohnung 2 vacant: it still consumed 1,000 kWh, the landlord bears 175.00 + 150.00 = 325.00 €
        Assert.Equal(675.00m, HeatingLine(result, 11, HeatingCostCalculator.HeatKey).Share);
        var landlord = Assert.Single(result.CostTypes.Single().Landlord);
        Assert.Equal((LandlordReason.Vacancy, "Leerstand Wohnung 2", 325.00m), (landlord.Reason, landlord.Label, landlord.Amount));
    }

    [Fact]
    public void Heating_not_agreed_in_the_lease_stays_with_the_landlord()
    {
        var input = TwoApartments(flatRateLease: false);
        var leases = input.Leases.Select(l => l.Id == Id(11) ? Lease(11, "A", 1, D(2024, 1, 1), excluded: "betrkv-04") : l).ToList();

        var result = Run(input with { Leases = leases }, Plain);

        Assert.Equal(0m, HeatingLine(result, 11, HeatingCostCalculator.HeatKey).Share);
        Assert.Equal(675.00m, HeatingLine(result, 11, HeatingCostCalculator.HeatKey).NotAgreed);
        Assert.Equal(LandlordReason.NotAgreed, Assert.Single(result.CostTypes.Single().Landlord).Reason);
    }

    // ---- shared flat -----------------------------------------------------------------------------------------------

    [Fact]
    public void Shared_flat_by_room_distributes_the_flat_by_area_and_days_over_the_rooms()
    {
        var input = Input(
            [Unit(4, "WG", 90m, null, SharedFlatBilling.ByRoom, Room(41, "Zimmer 1", 30m), Room(42, "Zimmer 2", 60m))],
            [Lease(21, "R1", 4, D(2024, 1, 1), room: 41), Lease(22, "R2", 4, D(2024, 1, 1), room: 42)],
            [HeatCost(101, "betrkv-04", 1000m, HeatingCostKind.Other)],
            [],
            Totals((4, MeterType.Heat, 3000m)));

        var result = Run(input, Plain);

        // 70 % = 700.00 € by room share 30 : 60 → 233.33 / 466.67; 30 % = 300.00 € by area → 100.00 / 200.00
        // R1 333.3333, R2 666.6667 → 333.33 and 666.67
        Assert.Equal(333.33m, HeatingLine(result, 21, HeatingCostCalculator.HeatKey).Share);
        Assert.Equal(666.67m, HeatingLine(result, 22, HeatingCostCalculator.HeatKey).Share);
    }

    [Fact]
    public void Shared_flat_by_room_gives_a_vacant_room_no_consumption_but_its_area_share()
    {
        var input = Input(
            [Unit(4, "WG", 90m, null, SharedFlatBilling.ByRoom, Room(41, "Zimmer 1", 30m), Room(42, "Zimmer 2", 60m))],
            [Lease(21, "R1", 4, D(2024, 1, 1), room: 41)],
            [HeatCost(101, "betrkv-04", 1000m, HeatingCostKind.Other)],
            [],
            Totals((4, MeterType.Heat, 3000m)));

        var result = Run(input, Plain);

        // Zimmer 2 is vacant all year. The measured consumption (70 % = 700.00 €) was caused by R1 alone; the area part (30 % = 300.00 €)
        // is split by room area 30 : 60 → R1 100.00 €, vacancy Zimmer 2 200.00 € (landlord).
        Assert.Equal(800.00m, HeatingLine(result, 21, HeatingCostCalculator.HeatKey).Share);
        var landlord = result.CostTypes.Single(t => t.CostType == HeatingCostCalculator.HeatKey).Landlord;
        Assert.Equal(200.00m, landlord.Where(p => p.Reason == LandlordReason.Vacancy).Sum(p => p.Amount));
    }

    [Fact]
    public void Shared_flat_with_flat_rate_is_not_billed_and_warns_about_the_reduction_right()
    {
        var input = Input(
            [Unit(4, "WG", 90m, null, SharedFlatBilling.FlatRate, Room(41, "Zimmer 1", 30m), Room(42, "Zimmer 2", 60m))],
            [Lease(21, "R1", 4, D(2024, 1, 1), room: 41, statement: false), Lease(22, "R2", 4, D(2024, 1, 1), room: 42, statement: false)],
            [HeatCost(101, "betrkv-04", 1000m, HeatingCostKind.Other)],
            [],
            Totals((4, MeterType.Heat, 3000m)));

        var result = Run(input, Plain);

        Assert.Empty(result.Tenants);
        Assert.Equal(2, result.Problems.Count(p => p.Text.Contains("§ 12 Abs. 1 HeizkostenV", StringComparison.Ordinal)));
        Assert.All(result.CostTypes.Single().Landlord, p => Assert.Equal(LandlordReason.SharedFlatFlatRate, p.Reason));
        Assert.Equal(1000m, result.FlatRates.Sum(f => f.ActualCosts));
    }

    // ---- settings --------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(50)]
    [InlineData(60)]
    [InlineData(70)]
    public void Settings_accept_a_consumption_share_from_50_to_70_percent(int percent)
    {
        new HeatingSettings(Guid.NewGuid()) { ConsumptionPercent = percent }.Validate();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(49.99)]
    [InlineData(70.01)]
    [InlineData(100)]
    public void Settings_reject_a_consumption_share_outside_50_to_70_percent(double percent)
    {
        var settings = new HeatingSettings(Guid.NewGuid()) { ConsumptionPercent = (decimal)percent };

        var exception = Assert.Throws<RuleViolationException>(settings.Validate);
        Assert.Contains("50 und 70", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_default_to_gas_with_70_30_and_the_formula_of_section_9()
    {
        var settings = new HeatingSettings(Guid.NewGuid());

        Assert.Equal((HeatingFuel.Gas, 70m, 2.5m, 60m, true), (settings.Fuel, settings.ConsumptionPercent, settings.HotWaterFormulaFactor, settings.HotWaterTemperature, settings.JointPlant));
        settings.Validate();
    }

    // ---- random check ----------------------------------------------------------------------------------------------

    [Fact]
    public void Random_houses_always_add_up_to_the_cent()
    {
        var random = new Random(30);
        for (var run = 0; run < 200; run++)
        {
            var unitCount = random.Next(1, 5);
            var units = Enumerable.Range(1, unitCount).Select(i => Unit(i, $"Whg {i}", random.Next(30, 120) + (random.Next(0, 100) / 100m))).ToList();
            var leases = new List<BillingLease>();
            var id = 10;
            foreach (var unit in units)
            {
                var number = int.Parse(unit.Name[4..], System.Globalization.CultureInfo.InvariantCulture);
                var change = random.Next(0, 3) == 0 ? D(2026, random.Next(2, 12), random.Next(1, 28)) : (DateOnly?)null;
                if (random.Next(0, 6) != 0)
                {
                    leases.Add(Lease(id++, $"M{id}", number, D(2020, 1, 1), change?.AddDays(-1)));
                    if (change is { } start)
                    {
                        leases.Add(Lease(id++, $"M{id}", number, start, statement: random.Next(0, 5) != 0));
                    }
                }
            }

            var joint = random.Next(0, 2) == 0;
            var costs = new List<BillingCost>
            {
                HeatCost(101, joint ? "betrkv-06" : "betrkv-04", random.Next(10000, 900000) / 100m, HeatingCostKind.Fuel, receipt: 1, quantity: random.Next(8000, 40000), unit: "kWh", co2Kg: random.Next(2000, 15000)),
                HeatCost(102, joint ? "betrkv-06" : "betrkv-04", random.Next(1000, 60000) / 100m, HeatingCostKind.CarbonPrice, receipt: 1, quantity: 1m, unit: "kWh", co2Kg: 1m),
                HeatCost(103, "betrkv-05", random.Next(100, 20000) / 100m, HeatingCostKind.Other),
            };
            var heat = units.Select(u => (Unit: int.Parse(u.Name[4..], System.Globalization.CultureInfo.InvariantCulture), Type: MeterType.Heat, Value: random.Next(500, 6000) / 3m)).ToList();
            var water = units.Select(u => (Unit: int.Parse(u.Name[4..], System.Globalization.CultureInfo.InvariantCulture), Type: MeterType.HotWater, Value: random.Next(5, 90) / 3m)).ToList();
            var input = Input(units, leases, costs, [], Totals([.. heat, .. water]));

            var result = Run(input, new HeatingParameters { JointPlant = joint, ConsumptionPercent = random.Next(50, 71), HotWaterHeat = new ConsumptionQuantity(random.Next(1000, 6000)) });

            foreach (var type in result.CostTypes.Where(t => t.Group == CostGroup.Heating))
            {
                var tenants = result.Tenants.SelectMany(t => t.Lines).Where(l => l.CostType == type.CostType).Sum(l => l.Share);
                Assert.Equal(type.Total, tenants + type.LandlordTotal);
                Assert.Equal(type.TenantTotal, tenants);
            }
        }
    }
}
