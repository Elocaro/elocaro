using Elocaro.Domain.Heating;
using Elocaro.Domain.Metering;
using Elocaro.Domain.OperatingCosts;
using static Elocaro.Domain.Tests.Heating.HeatingScenario;
using static Elocaro.Domain.Tests.OperatingCosts.Scenario;

namespace Elocaro.Domain.Tests.Heating;

/// <summary>CO₂ cost split (CO2KostAufG): levels, boundaries, special cases.</summary>
public class CarbonCostTests
{
    [Theory]
    [InlineData("0", 1, "0")]
    [InlineData("11.94", 1, "0")]
    [InlineData("12", 2, "10")]
    [InlineData("16.94", 2, "10")]
    [InlineData("17", 3, "20")]
    [InlineData("21.94", 3, "20")]
    [InlineData("22", 4, "30")]
    [InlineData("26.94", 4, "30")]
    [InlineData("27", 5, "40")]
    [InlineData("31.94", 5, "40")]
    [InlineData("32", 6, "50")]
    [InlineData("36.94", 6, "50")]
    [InlineData("37", 7, "60")]
    [InlineData("41.94", 7, "60")]
    [InlineData("42", 8, "70")]
    [InlineData("46.94", 8, "70")]
    [InlineData("47", 9, "80")]
    [InlineData("51.94", 9, "80")]
    [InlineData("52", 10, "95")]
    [InlineData("150", 10, "95")]
    public void Every_level_boundary_belongs_to_the_higher_level(string specific, int level, string landlord)
    {
        // 100 m² living area, so the emissions in kg are 100 × the specific value.
        var kg = decimal.Parse(specific, System.Globalization.CultureInfo.InvariantCulture) * 100m;

        var result = CarbonCostCalculator.Classify(CarbonCostTables.For(2026)!, kg, 100m, 365);

        Assert.Equal(level, result.Level.Number);
        Assert.Equal(decimal.Parse(landlord, System.Globalization.CultureInfo.InvariantCulture), result.Level.LandlordPercent);
        Assert.Equal(100m - result.Level.LandlordPercent, result.Level.TenantPercent);
    }

    [Theory]
    [InlineData("11.949", 1)]
    [InlineData("11.95", 2)] // 11.95 → 12.0 → level 2
    [InlineData("11.99", 2)]
    [InlineData("51.95", 10)]
    [InlineData("51.94", 9)]
    public void Specific_emissions_are_rounded_to_one_decimal_before_the_level_is_looked_up(string specific, int level)
    {
        // § 5 para. 1 sentence 3 CO2KostAufG
        var kg = decimal.Parse(specific, System.Globalization.CultureInfo.InvariantCulture) * 100m;

        var result = CarbonCostCalculator.Classify(CarbonCostTables.For(2026)!, kg, 100m, 365);

        Assert.Equal(level, result.Level.Number);
        Assert.Equal(decimal.Round(result.SpecificKg, 1), result.SpecificKg);
    }

    [Fact]
    public void A_short_period_reduces_the_level_bounds_pro_rata_and_rounds_the_measured_value()
    {
        // § 5 para. 1 sentence 4: 181 days → level 2 starts at 12 × 181/365 = 5.9507 kg/m².
        // 594 kg ÷ 100 m² = 5.94 → 5.9 → level 1; 595 kg → 5.95 → 6.0 → level 2 (annualised without rounding it would be 11.9986)
        var table = CarbonCostTables.For(2026)!;

        Assert.Equal(1, CarbonCostCalculator.Classify(table, 594m, 100m, 181).Level.Number);
        Assert.Equal(2, CarbonCostCalculator.Classify(table, 595m, 100m, 181).Level.Number);
    }

    [Fact]
    public void Table_has_ten_gapless_levels_and_is_marked_as_not_verified()
    {
        var table = CarbonCostTables.For(2026)!;

        Assert.Equal(10, table.Levels.Count);
        Assert.Equal(Enumerable.Range(1, 10), table.Levels.Select(l => l.Number));
        Assert.Equal(0m, table.Levels[0].From);
        for (var i = 0; i + 1 < table.Levels.Count; i++)
        {
            Assert.Equal(table.Levels[i].To, table.Levels[i + 1].From);
            Assert.True(table.Levels[i].LandlordPercent < table.Levels[i + 1].LandlordPercent);
        }

        Assert.Null(table.Levels[^1].To);
        Assert.Equal(95m, table.Levels[^1].LandlordPercent);
        Assert.False(table.Verified);
        Assert.Contains("Stand/prüfen", table.Status, StringComparison.Ordinal);
        Assert.Equal("unter 12 kg/(m²·a)", table.Levels[0].Range.Replace(" ", " ", StringComparison.Ordinal));
        Assert.Equal("52 kg/(m²·a) und mehr", table.Levels[^1].Range);
    }

    [Fact]
    public void Table_is_versioned_by_billing_year_and_does_not_exist_before_the_law()
    {
        Assert.Null(CarbonCostTables.For(2022));
        Assert.Equal("CO2KostAufG-2023", CarbonCostTables.For(2023)!.Version);
        Assert.Equal(CarbonCostTables.For(2023), CarbonCostTables.For(2030));
    }

    [Fact]
    public void A_period_that_is_not_a_year_is_converted_to_a_year()
    {
        // 181 days: 1,000 kg ÷ 100 m² = 10 kg × 365/181 = 20.17 kg/(m²·a) → level 3
        var result = CarbonCostCalculator.Classify(CarbonCostTables.For(2026)!, 1000m, 100m, 181);

        Assert.Equal(3, result.Level.Number);
        Assert.Equal(20.17m, decimal.Round(result.SpecificKg, 2));
        Assert.Equal(1m, CarbonCostCalculator.Classify(CarbonCostTables.For(2028)!, 1000m, 100m, 366).YearFactor);
    }

    private static BillingInput House(decimal co2Kg, decimal carbonCost = 360m)
    {
        var consumption = RuleConsumption.Totals(
            Year2026,
            (1, MeterType.Heat, 1000m),
            (1, MeterType.HotWater, 10m));
        return Input(
            [Unit(1, "Wohnung 1", 100m)],
            [Lease(11, "A", 1, D(2024, 1, 1))],
            [
                HeatCost(101, "betrkv-06", 2000m, HeatingCostKind.Fuel, receipt: 1, quantity: 10000m, unit: "kWh", co2Kg: co2Kg),
                HeatCost(102, "betrkv-06", carbonCost, HeatingCostKind.CarbonPrice, receipt: 1, quantity: 10000m, unit: "kWh", co2Kg: co2Kg),
            ],
            [],
            consumption);
    }

    private static readonly HeatingParameters Measured = new() { HotWaterHeat = new ConsumptionQuantity(2000m) };

    [Fact]
    public void Level_1_has_no_landlord_share_and_still_shows_the_classification()
    {
        // 1,000 kg ÷ 100 m² = 10 kg/(m²·a) → level 1
        var result = Run(House(1000m), Measured);

        var line = HeatingLine(result, 11, HeatingCostCalculator.CarbonKey);
        Assert.Equal(0m, line.Share);
        Assert.Contains("Stufe 1", line.Key, StringComparison.Ordinal);
        Assert.Empty(result.CostTypes.Single(t => t.CostType == HeatingCostCalculator.CarbonKey).Landlord);
    }

    [Fact]
    public void Level_10_landlord_bears_95_percent_of_the_tenants_carbon_costs()
    {
        // 6,000 kg ÷ 100 m² = 60 kg → level 10: 95 %. Gross costs 2,360.00 €, of which CO₂ 360.00 €: deduction 360 × 95 % = 342.00 €.
        var result = Run(House(6000m), Measured);

        Assert.Equal(-342.00m, HeatingLine(result, 11, HeatingCostCalculator.CarbonKey).Share);
        Assert.Equal(342.00m, result.CostTypes.Single(t => t.CostType == HeatingCostCalculator.CarbonKey).LandlordTotal);
        Assert.Equal(2360m - 342m, HeatingBalance(result, 11).Share);
    }

    [Fact]
    public void Missing_carbon_amount_is_an_error_when_carbon_costs_exist()
    {
        var result = Run(House(0m), Measured with { });

        Assert.Contains(result.Problems, p => p.Severity == ProblemSeverity.Error && p.Text.Contains("CO₂-Menge in kg fehlt", StringComparison.Ordinal));
        Assert.DoesNotContain(result.CostTypes, t => t.CostType == HeatingCostCalculator.CarbonKey);
    }

    [Theory]
    [InlineData(CarbonSpecialCase.ProtectedBuilding, "denkmalgeschützt")]
    [InlineData(CarbonSpecialCase.HighlyEfficient, "Passivhaus")]
    [InlineData(CarbonSpecialCase.NotSubject, "nicht vom CO2KostAufG erfasst")]
    public void Special_cases_only_warn_and_do_not_change_the_numbers(CarbonSpecialCase special, string text)
    {
        var normal = Run(House(6000m), Measured);
        var flagged = Run(House(6000m), Measured with { CarbonSpecialCase = special });

        Assert.Contains(flagged.Problems, p => p.Severity == ProblemSeverity.Warning && p.Text.Contains(text, StringComparison.Ordinal));
        Assert.Equal(HeatingLine(normal, 11, HeatingCostCalculator.CarbonKey).Share, HeatingLine(flagged, 11, HeatingCostCalculator.CarbonKey).Share);
        Assert.DoesNotContain(normal.Problems, p => p.Text.Contains(text, StringComparison.Ordinal));
    }

    [Fact]
    public void Non_fossil_supply_or_switched_off_has_no_carbon_split()
    {
        Assert.DoesNotContain(Run(House(6000m), Measured with { Fuel = HeatingFuel.Other }).CostTypes, t => t.CostType == HeatingCostCalculator.CarbonKey);
        Assert.DoesNotContain(Run(House(6000m), Measured with { ApplyCarbonCosts = false }).CostTypes, t => t.CostType == HeatingCostCalculator.CarbonKey);
    }

    [Fact]
    public void Before_2023_there_is_no_carbon_split()
    {
        var input = House(6000m) with { BillingPeriod = new Elocaro.Domain.Common.Period(D(2022, 1, 1), D(2022, 12, 31)) };
        var consumption = new RuleConsumption((u, t, p) => new ConsumptionQuantity(t == MeterType.Heat ? 1000m : 10m));
        var costs = input.Costs.Select(c => c with { BillingPeriod = input.BillingPeriod }).ToList();

        var result = Run(input with { Consumption = consumption, Costs = costs, Leases = input.Leases }, Measured);

        Assert.DoesNotContain(result.CostTypes, t => t.CostType == HeatingCostCalculator.CarbonKey);
    }

    [Fact]
    public void A_period_that_began_before_2023_has_no_carbon_split_even_if_it_ends_in_2023()
    {
        // § 11 para. 1 CO2KostAufG: the law applies to billing periods that begin on or after 1 January 2023
        var period = new Elocaro.Domain.Common.Period(D(2022, 7, 1), D(2023, 6, 30));
        var consumption = new RuleConsumption((u, t, p) => new ConsumptionQuantity(t == MeterType.Heat ? 1000m : 10m));
        var input = House(6000m);
        var costs = input.Costs.Select(c => c with { BillingPeriod = period }).ToList();

        var result = Run(input with { BillingPeriod = period, Consumption = consumption, Costs = costs }, Measured);

        Assert.DoesNotContain(result.CostTypes, t => t.CostType == HeatingCostCalculator.CarbonKey);
        Assert.DoesNotContain(result.Problems, p => p.CostType == HeatingCostCalculator.CarbonKey);
    }
}
