using MietHahn.Domain.Heating;
using MietHahn.Domain.Leases;
using MietHahn.Domain.Metering;
using MietHahn.Domain.OperatingCosts;
using static MietHahn.Domain.Tests.Heating.HeatingScenario;
using static MietHahn.Domain.Tests.OperatingCosts.Scenario;

namespace MietHahn.Domain.Tests.Heating;

/// <summary>
/// Golden master of the heating cost core, calculated by hand (arithmetic in the comments).
/// Worked example in the style of the specialist literature (invented figures, not a published example):
/// house with three apartments, billing year 2026, central gas boiler for heating and hot water, heat meter in the
/// hot water circuit, 70 % consumption / 30 % base costs.
/// <code>
/// Wohnung 1  60 m²  A   heat 7,000 kWh   hot water 40 m³   heating prepayments 100 €/month
/// Wohnung 2  80 m²  B   heat 9,000 kWh   hot water 50 m³   heating prepayments 120 €/month
/// Wohnung 3  60 m²  C   heat 4,000 kWh   hot water 30 m³   heating prepayments  70 €/month
/// Costs: gas 3,000.00 € (30,000 kWh, 6,000 kg CO₂), CO₂ costs 360.00 € (60 €/t), operating power 60 €, maintenance 100 €,
///        flue gas measurement 40 €, meter rental 40 € → 3,600.00 €. Heat quantity of the hot water circuit: 6,000 kWh.
/// </code>
/// </summary>
public class HeatingGoldenMasterTests
{
    private const int Apartment1 = 1;
    private const int Apartment2 = 2;
    private const int Apartment3 = 3;
    private const int A = 11;
    private const int B = 12;
    private const int C = 13;
    private const string Heat = HeatingCostCalculator.HeatKey;
    private const string HotWater = HeatingCostCalculator.HotWaterKey;
    private const string Carbon = HeatingCostCalculator.CarbonKey;

    private static BillingInput House(bool carbonCosts = true, bool measuredHotWaterHeat = true)
    {
        var units = new[] { Unit(Apartment1, "Wohnung 1", 60m), Unit(Apartment2, "Wohnung 2", 80m), Unit(Apartment3, "Wohnung 3", 60m) };
        var leases = new[]
        {
            Lease(A, "A", Apartment1, D(2024, 1, 1), prepayments: Months(2026, 1, 12, 100m, RentComponentType.HeatingCostPrepayment)),
            Lease(B, "B", Apartment2, D(2024, 1, 1), prepayments: Months(2026, 1, 12, 120m, RentComponentType.HeatingCostPrepayment)),
            Lease(C, "C", Apartment3, D(2024, 1, 1), prepayments: Months(2026, 1, 12, 70m, RentComponentType.HeatingCostPrepayment)),
        };
        var costs = new List<BillingCost>
        {
            HeatCost(101, "betrkv-06", 3000m, HeatingCostKind.Fuel, receipt: 1, quantity: 30000m, unit: "kWh", co2Kg: 6000m, description: "Gas 2026"),
            HeatCost(103, "betrkv-06", 60m, HeatingCostKind.OperatingPower, description: "Betriebsstrom"),
            HeatCost(104, "betrkv-06", 100m, HeatingCostKind.Maintenance, description: "Wartung"),
            HeatCost(105, "betrkv-06", 40m, HeatingCostKind.EmissionMeasurement, description: "Abgasmessung"),
            HeatCost(106, "betrkv-06", 40m, HeatingCostKind.MeterService, description: "Zählermiete"),
        };
        if (carbonCosts)
        {
            // Second item of the same gas receipt: the receipt data (kWh, kg) must count once.
            costs.Add(HeatCost(102, "betrkv-06", 360m, HeatingCostKind.CarbonPrice, receipt: 1, quantity: 30000m, unit: "kWh", co2Kg: 6000m, description: "CO₂-Kosten 2026"));
        }

        var consumption = RuleConsumption.Totals(
            Year2026,
            (Apartment1, MeterType.Heat, 7000m),
            (Apartment2, MeterType.Heat, 9000m),
            (Apartment3, MeterType.Heat, 4000m),
            (Apartment1, MeterType.HotWater, 40m),
            (Apartment2, MeterType.HotWater, 50m),
            (Apartment3, MeterType.HotWater, 30m));
        return Input(units, leases, costs, [], consumption);
    }

    private static HeatingParameters Parameters(bool measured = true) =>
        new() { HotWaterHeat = measured ? new ConsumptionQuantity(6000m, false, "Wärmezähler WW") : null };

    [Fact]
    public void Worked_example_total_costs_hot_water_split_and_distribution_by_hand()
    {
        var result = Run(House(), Parameters());

        // Total costs: 3,000.00 + 360.00 + 60.00 + 100.00 + 40.00 + 40.00 = 3,600.00 €.
        // § 9: hot water share = 6,000 kWh ÷ 30,000 kWh = 20 % → hot water 720.00 €, heating 2,880.00 €.
        var heat = result.CostTypes.Single(t => t.CostType == Heat);
        var water = result.CostTypes.Single(t => t.CostType == HotWater);
        Assert.Equal(2880.00m, heat.Total);
        Assert.Equal(720.00m, water.Total);
        Assert.Equal(3600.00m, heat.Total + water.Total);

        // Heating: 70 % by heat quantity = 2,016.00 € ÷ 20,000 kWh (7,000 / 9,000 / 4,000), 30 % by heated area = 864.00 € ÷ 200 m².
        // A  2,016 × 7/20 = 705.60   + 864 × 60/200 = 259.20 → 964.80
        // B  2,016 × 9/20 = 907.20   + 864 × 80/200 = 345.60 → 1,252.80
        // C  2,016 × 4/20 = 403.20   + 864 × 60/200 = 259.20 → 662.40      Σ = 2,880.00
        Assert.Equal(964.80m, HeatingLine(result, A, Heat).Share);
        Assert.Equal(1252.80m, HeatingLine(result, B, Heat).Share);
        Assert.Equal(662.40m, HeatingLine(result, C, Heat).Share);

        // Hot water: 70 % by m³ = 504.00 € ÷ 120 m³ (40 / 50 / 30), 30 % by living area = 216.00 € ÷ 200 m².
        // A  504 × 40/120 = 168.00 + 216 × 60/200 = 64.80 → 232.80
        // B  504 × 50/120 = 210.00 + 216 × 80/200 = 86.40 → 296.40
        // C  504 × 30/120 = 126.00 + 64.80               → 190.80      Σ = 720.00
        Assert.Equal(232.80m, HeatingLine(result, A, HotWater).Share);
        Assert.Equal(296.40m, HeatingLine(result, B, HotWater).Share);
        Assert.Equal(190.80m, HeatingLine(result, C, HotWater).Share);
        Assert.Equal(0m, heat.LandlordTotal);
        Assert.False(result.HasErrors);
    }

    [Fact]
    public void Carbon_costs_level_5_landlord_share_is_deducted_and_shown_separately()
    {
        var result = Run(House(), Parameters());

        // CO₂: 6,000 kg ÷ 200 m² = 30 kg/(m²·a) → level 5 (27 to under 32): landlord 40 %, tenants 60 %.
        // CO₂ costs 360.00 € are 10 % of the total costs, so every tenant carries 10 % of the gross share as CO₂ costs.
        // A  (964.80 + 232.80) × 10 % = 119.76 → × 40 % = 47.904     B  (1,252.80 + 296.40) × 10 % = 154.92 → 61.968
        // C  (662.40 + 190.80) × 10 % =  85.32 → 34.128              Σ = 144.000 = 360.00 × 40 %
        // Rounded down: 47.90 + 61.96 + 34.12 = 143.98 → 2 cents to the largest fractional parts (B .8, C .8) → 61.97 and 34.13.
        Assert.Equal(-47.90m, HeatingLine(result, A, Carbon).Share);
        Assert.Equal(-61.97m, HeatingLine(result, B, Carbon).Share);
        Assert.Equal(-34.13m, HeatingLine(result, C, Carbon).Share);

        var carbon = result.CostTypes.Single(t => t.CostType == Carbon);
        Assert.Equal(-144.00m, carbon.TenantTotal);
        Assert.Equal(144.00m, carbon.LandlordTotal);
        var position = Assert.Single(carbon.Landlord);
        Assert.Equal(LandlordReason.CarbonCostShare, position.Reason);
        Assert.Contains("Stufe 5", position.Label, StringComparison.Ordinal);
        Assert.Contains("Stufe 5", HeatingLine(result, A, Carbon).Key, StringComparison.Ordinal);
        Assert.Contains(HeatingLine(result, A, Carbon).Protocol, e => e.Title == "Einstufung nach CO2KostAufG" && e.Values.Contains("30,00 kg/(m²·a)", StringComparison.Ordinal));

        var section = result.Sections.Single(s => s.Id == "carbon");
        Assert.Contains("Stand/prüfen", section.Notice, StringComparison.Ordinal);
        Assert.Contains(result.Problems, p => p.Severity == ProblemSeverity.Warning && p.Text.Contains("Stand/prüfen", StringComparison.Ordinal));
    }

    [Fact]
    public void Tenant_balances_net_the_heating_prepayments_after_the_carbon_deduction()
    {
        var result = Run(House(), Parameters());

        // Net heating costs: A 964.80 + 232.80 − 47.90 = 1,149.70    B 1,252.80 + 296.40 − 61.97 = 1,487.23    C 662.40 + 190.80 − 34.13 = 819.07
        // Prepayments 12 months: A 1,200.00, B 1,440.00, C 840.00 → balance A −50.30 (credit), B +47.23, C −20.93.
        var a = HeatingBalance(result, A);
        var b = HeatingBalance(result, B);
        var c = HeatingBalance(result, C);
        Assert.True(a.Calculated);
        Assert.Equal([1149.70m, 1487.23m, 819.07m], [a.Share, b.Share, c.Share]);
        Assert.Equal([1200m, 1440m, 840m], [a.PrepaymentsPaid, b.PrepaymentsPaid, c.PrepaymentsPaid]);
        Assert.Equal([-50.30m, 47.23m, -20.93m], [a.Balance, b.Balance, c.Balance]);

        // Σ net = 3,456.00 = 3,600.00 − 144.00 (landlord's CO₂ share)
        Assert.Equal(3456.00m, a.Share + b.Share + c.Share);
        Assert.Empty(result.UncalculatedGroups);

        // The operating cost balance does not contain the heating prepayments.
        Assert.Equal(0m, result.Tenants.Single(t => t.LeaseId == Id(A)).Operating!.PrepaymentsPaid);
    }

    [Fact]
    public void Without_hot_water_heat_meter_the_formula_of_section_9_splits_the_plant()
    {
        // Q = 2.5 · V · (t − 10) = 2.5 × 120 m³ × (60 − 10) K = 15,000 kWh; share = 15,000 ÷ 30,000 = 50 %.
        // Heating 1,800.00 € (70 %: 1,260.00 € ÷ 20,000 kWh; 30 %: 540.00 € ÷ 200 m²), hot water 1,800.00 €.
        var result = Run(House(), Parameters(measured: false));

        Assert.Equal(1800.00m, result.CostTypes.Single(t => t.CostType == Heat).Total);
        Assert.Equal(1800.00m, result.CostTypes.Single(t => t.CostType == HotWater).Total);
        // A heating: 1,260 × 7/20 = 441.00 + 540 × 60/200 = 162.00 → 603.00
        Assert.Equal(603.00m, HeatingLine(result, A, Heat).Share);
        var entry = Assert.Single(result.Sections.Single(s => s.Id == "heating").Entries, e => e.Title.StartsWith("Aufteilung Heizung/Warmwasser", StringComparison.Ordinal));
        Assert.Contains("Formel Q = 2,5 × V × (t − 10)", entry.Values, StringComparison.Ordinal);
        Assert.Equal(50m, entry.Result);
    }

    [Fact]
    public void Meter_sum_basis_divides_the_hot_water_heat_by_the_heat_of_heating_and_hot_water_meters()
    {
        // Heating meters 7,000 + 9,000 + 4,000 = 20,000 kWh, hot water 6,000 kWh: share = 6,000 ÷ 26,000 = 23.0769 %.
        // Total 3,600.00 € → hot water 830.77 €, heating 2,769.23 €.
        var parameters = new HeatingParameters
        {
            HotWaterHeat = new ConsumptionQuantity(6000m, false, "Wärmezähler WW"),
            SplitBasis = HotWaterSplitBasis.MeterSum,
            HeatingHeat = new ConsumptionQuantity(20000m, false, "Zähler der Wohnungen"),
        };

        var result = Run(House(), parameters);

        Assert.Equal(2769.23m, result.CostTypes.Single(t => t.CostType == Heat).Total);
        Assert.Equal(830.77m, result.CostTypes.Single(t => t.CostType == HotWater).Total);
        var entry = Assert.Single(result.Sections.Single(s => s.Id == "heating").Entries, e => e.Title.StartsWith("Aufteilung Heizung/Warmwasser", StringComparison.Ordinal));
        Assert.Contains("Wärmezählern", entry.Formula, StringComparison.Ordinal);
        Assert.Equal(23.0769m, entry.Result);
        Assert.False(result.HasErrors);
    }

    [Fact]
    public void Meter_sum_basis_without_heating_heat_falls_back_to_the_fuel_energy_and_reports_it()
    {
        var parameters = new HeatingParameters
        {
            HotWaterHeat = new ConsumptionQuantity(6000m, false, "Wärmezähler WW"),
            SplitBasis = HotWaterSplitBasis.MeterSum,
            HeatingHeat = new ConsumptionQuantity(null, false, "Der Einheit ist kein Zähler vom Typ „Wärme“ zugeordnet."),
        };

        var result = Run(House(), parameters);

        Assert.Equal(720.00m, result.CostTypes.Single(t => t.CostType == HotWater).Total); // 6,000 ÷ 30,000 = 20 %
        Assert.Contains(result.Problems, p => p.Severity == ProblemSeverity.Error && p.Text.Contains("Wärmezähler der Heizung", StringComparison.Ordinal));
    }

    [Fact]
    public void Formula_factor_and_temperature_can_be_changed()
    {
        // Q = 2.6 × 120 × (55 − 10) = 14,040 kWh; 14,040 ÷ 30,000 = 46.8 %
        var result = Run(House(), new HeatingParameters { HotWaterFormulaFactor = 2.6m, HotWaterTemperature = 55m });

        Assert.Equal(46.8m, result.Sections.Single(s => s.Id == "heating").Entries.Single(e => e.Title.StartsWith("Aufteilung", StringComparison.Ordinal)).Result);
    }

    [Fact]
    public void Consumption_share_must_be_between_50_and_70_percent()
    {
        foreach (var invalid in new[] { 49m, 71m, 100m })
        {
            var result = Run(House(), new HeatingParameters { ConsumptionPercent = invalid, HotWaterHeat = new ConsumptionQuantity(6000m) });
            Assert.Contains(result.Problems, p => p.Severity == ProblemSeverity.Error && p.Text.Contains("nicht zulässig", StringComparison.Ordinal));
        }

        // 50 % is allowed: heating 1,440.00 by kWh (A 504.00) + 1,440.00 by area (A 432.00) → 936.00
        var fifty = Run(House(), new HeatingParameters { ConsumptionPercent = 50m, HotWaterHeat = new ConsumptionQuantity(6000m) });
        Assert.False(fifty.HasErrors);
        Assert.Equal(936.00m, HeatingLine(fifty, A, Heat).Share);
    }

    [Fact]
    public void Missing_fuel_quantity_or_meter_is_reported_as_an_error()
    {
        var input = House();
        var noQuantity = input with { Costs = [.. input.Costs.Select(c => c with { FuelQuantity = null, FuelUnit = null })] };

        var result = Run(noQuantity, Parameters());

        Assert.True(result.HasErrors);
        Assert.Contains(result.Problems, p => p.Severity == ProblemSeverity.Error && p.Text.Contains("Brennstoffmenge fehlt", StringComparison.Ordinal));

        var noMeter = Run(input with { Consumption = RuleConsumption.Totals(Year2026, (Apartment1, MeterType.Heat, 1m)) }, Parameters());
        Assert.Contains(noMeter.Problems, p => p.Severity == ProblemSeverity.Error && p.Text.Contains("Wohnung 2", StringComparison.Ordinal));
    }

    [Fact]
    public void Without_carbon_costs_nothing_is_deducted_and_a_hint_is_given()
    {
        var result = Run(House(carbonCosts: false), Parameters());

        Assert.DoesNotContain(result.CostTypes, t => t.CostType == Carbon);
        Assert.Contains(result.Problems, p => p.Text.Contains("keine CO₂-Kosten erfasst", StringComparison.Ordinal));
    }
}
