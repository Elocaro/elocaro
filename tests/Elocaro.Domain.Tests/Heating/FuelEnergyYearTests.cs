using Elocaro.Domain.Common;
using Elocaro.Domain.Heating;

namespace Elocaro.Domain.Tests.Heating;

public class FuelEnergyYearTests
{
    [Fact]
    public void The_energy_per_cubic_meter_of_the_billing_year_replaces_the_fixed_value()
    {
        var settings = new HeatingSettings(Guid.NewGuid()) { FuelKwhPerUnit = 10m, EnergyYears = [new FuelEnergyYear(2025, 11.2m, 0.95m), new FuelEnergyYear(2024, 11.0m, 0.96m)] };

        Assert.Equal(10.64m, settings.KwhPerUnitFor(Period.CalendarYear(2025)));
        Assert.Equal(10.56m, settings.KwhPerUnitFor(Period.CalendarYear(2024)));
        Assert.Equal(10m, settings.KwhPerUnitFor(Period.CalendarYear(2023)));
    }

    [Fact]
    public void Implausible_or_duplicate_years_are_rejected()
    {
        var duplicate = new HeatingSettings(Guid.NewGuid()) { EnergyYears = [new FuelEnergyYear(2025, 11m, 0.9m), new FuelEnergyYear(2025, 11m, 0.9m)] };
        Assert.Throws<RuleViolationException>(duplicate.Validate);
        var implausible = new HeatingSettings(Guid.NewGuid()) { EnergyYears = [new FuelEnergyYear(2025, 112m, 0.9m)] };
        Assert.Throws<RuleViolationException>(implausible.Validate);
    }
}
