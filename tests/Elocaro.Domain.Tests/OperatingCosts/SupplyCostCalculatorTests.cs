using Elocaro.Domain.Common;
using Elocaro.Domain.Heating;
using Elocaro.Domain.Metering;
using Elocaro.Domain.OperatingCosts;
using static Elocaro.Domain.Tests.OperatingCosts.Scenario;

namespace Elocaro.Domain.Tests.OperatingCosts;

public class SupplyCostCalculatorTests
{
    private static readonly Guid Property = Id(1);
    private static readonly Period Year = Period.CalendarYear(2025);

    private static SupplyContract Water() =>
        new(Property, "Wasser", "Stadt", "betrkv-02", MeterType.ColdWater, SupplyBasis.MainMeter, PriceUnit.CubicMeter, [Id(5)]);

    [Fact]
    public void Cost_is_consumption_times_price_plus_twelve_base_fees_for_a_full_year()
    {
        var contract = Water();
        contract.AddTariff(D(2025, 1, 1), 3.50m, 10m);

        var result = SupplyCostCalculator.Calculate(contract.ToModel(), Year, _ => (262.4m, 262.4m, null));

        var part = Assert.Single(result.Parts);
        Assert.Equal(12m, part.Months);
        Assert.Equal(1038.40m, part.Amount); // 262.4 × 3.50 = 918.40 + 12 × 10.00 = 120.00
        Assert.True(result.CoversWholePeriod);
        Assert.Empty(result.Problems);
        Assert.Contains("262,4 m³ × 3,5 €", part.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void A_price_change_splits_the_period_and_the_consumption_per_segment()
    {
        var contract = Water();
        contract.AddTariff(D(2024, 1, 1), 3.00m, 12m);
        contract.AddTariff(D(2025, 7, 1), 4.00m, 24m);

        // Consumption per segment: 100 m³ until 30.6., 60 m³ from 1.7.
        var result = SupplyCostCalculator.Calculate(contract.ToModel(), Year, p => p.From.Month == 1 ? (100m, 100m, null) : (60m, 60m, null));

        Assert.Equal([new Period(D(2025, 1, 1), D(2025, 6, 30)), new Period(D(2025, 7, 1), D(2025, 12, 31))], result.Parts.Select(p => p.Period));
        Assert.Equal([372m, 384m], result.Parts.Select(p => p.Amount)); // 100 × 3 + 6 × 12 | 60 × 4 + 6 × 24
        Assert.True(result.CoversWholePeriod);
    }

    [Fact]
    public void A_partial_month_counts_by_days_and_a_missing_reading_is_a_problem()
    {
        Assert.Equal(1.5m, SupplyCostCalculator.Months(new Period(D(2025, 1, 1), D(2025, 2, 14)))); // Jan full, Feb 14/28 = 0.5
        var contract = Water();
        contract.AddTariff(D(2025, 1, 1), 3m, 0m);

        var result = SupplyCostCalculator.Calculate(contract.ToModel(), Year, _ => (null, null, "Für Zähler 1 fehlt ein Stand."));

        Assert.Single(result.Problems);
        Assert.Contains("fehlt", result.Problems[0], StringComparison.Ordinal);
    }

    [Fact]
    public void A_tariff_that_starts_later_does_not_cover_the_whole_period()
    {
        var contract = Water();
        contract.AddTariff(D(2025, 4, 1), 3m, 0m);

        var result = SupplyCostCalculator.Calculate(contract.ToModel(), Year, _ => (10m, 10m, null));

        Assert.False(result.CoversWholePeriod);
    }

    [Fact]
    public void Tariffs_cannot_overlap_and_the_last_removal_reopens_the_previous_tariff()
    {
        var contract = Water();
        var first = contract.AddTariff(D(2024, 1, 1), 3m, 0m);
        var second = contract.AddTariff(D(2025, 1, 1), 4m, 0m);

        Assert.Equal(new Period(D(2024, 1, 1), D(2024, 12, 31)), first.Validity);
        Assert.Throws<RuleViolationException>(() => contract.AddTariff(D(2024, 6, 1), 5m, 0m));
        Assert.Throws<RuleViolationException>(() => contract.AddTariff(D(2026, 1, 1), -1m, 0m));
        Assert.Throws<RuleViolationException>(() => contract.AddTariff(D(2026, 1, 1), 0m, 0m));
        // A price typed with a decimal point in German notation (0.0905 → 905) is rejected.
        Assert.Throws<RuleViolationException>(() => contract.AddTariff(D(2026, 1, 1), 905m, 0m));

        contract.RemoveTariff(second.Id);

        Assert.True(first.Validity.IsOpenEnded);
    }

    [Fact]
    public void A_contract_needs_a_name_a_meter_an_allocable_cost_type_and_for_heating_a_kind()
    {
        Assert.Throws<RuleViolationException>(() => new SupplyContract(Property, " ", null, "betrkv-02", MeterType.ColdWater, SupplyBasis.MainMeter, PriceUnit.CubicMeter, [Id(5)]));
        Assert.Throws<RuleViolationException>(() => new SupplyContract(Property, "W", null, "betrkv-02", MeterType.ColdWater, SupplyBasis.MainMeter, PriceUnit.CubicMeter, []));
        Assert.Throws<RuleViolationException>(() => new SupplyContract(Property, "W", null, "verwaltung", MeterType.ColdWater, SupplyBasis.MainMeter, PriceUnit.CubicMeter, [Id(5)]));
        Assert.Throws<RuleViolationException>(() => new SupplyContract(Property, "Gas", null, "betrkv-04", MeterType.Gas, SupplyBasis.MainMeter, PriceUnit.KilowattHour, [Id(5)]));
        Assert.Throws<RuleViolationException>(() => new SupplyContract(Property, "W", null, "betrkv-02", MeterType.ColdWater, SupplyBasis.MainMeter, PriceUnit.KilowattHour, [Id(5)]));
        var gas = new SupplyContract(Property, "Gas", null, "betrkv-04", MeterType.Gas, SupplyBasis.MainMeter, PriceUnit.KilowattHour, [Id(5)], HeatingCostKind.Fuel);
        Assert.Equal(HeatingCostKind.Fuel, gas.HeatingKind);
    }
}
