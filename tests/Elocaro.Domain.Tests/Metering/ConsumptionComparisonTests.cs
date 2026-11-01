using Elocaro.Domain.Metering;

namespace Elocaro.Domain.Tests.Metering;

public class ConsumptionComparisonTests
{
    [Fact]
    public void The_average_needs_at_least_three_values()
    {
        Assert.Null(ConsumptionComparison.Average([]));
        Assert.Null(ConsumptionComparison.Average([6m, 4m]));
        Assert.Equal(5.333m, ConsumptionComparison.Average([6m, 6m, 4m]));
    }

    [Theory]
    [InlineData(300, 200, 50)]
    [InlineData(150, 200, -25)]
    [InlineData(200, 200, 0)]
    public void Change_is_given_in_whole_percent(double current, double reference, double expected) =>
        Assert.Equal((decimal)expected, ConsumptionComparison.ChangePercent((decimal)current, (decimal)reference));

    [Fact]
    public void Weather_adjusted_change_compares_the_consumption_per_degree_day()
    {
        // 300 kWh in a month with 400 degree days (0.75) against 200 kWh with 500 (0.4): +88 % although the raw change is +50 %.
        Assert.Equal(88m, ConsumptionComparison.WeatherAdjustedChangePercent(300m, 400m, 200m, 500m));
        // Colder than the year before, same consumption per degree day: no change.
        Assert.Equal(0m, ConsumptionComparison.WeatherAdjustedChangePercent(300m, 600m, 200m, 400m));
    }

    [Theory]
    [InlineData(null, 400.0, 200.0, 500.0)]
    [InlineData(300.0, null, 200.0, 500.0)]
    [InlineData(300.0, 400.0, null, 500.0)]
    [InlineData(300.0, 400.0, 200.0, null)]
    [InlineData(300.0, 10.0, 200.0, 500.0)]
    [InlineData(300.0, 400.0, 200.0, 0.0)]
    [InlineData(300.0, 400.0, 0.0, 500.0)]
    public void Weather_adjusted_change_is_missing_without_values_or_with_hardly_any_degree_days(double? current, double? currentDegreeDays, double? reference, double? referenceDegreeDays) =>
        Assert.Null(ConsumptionComparison.WeatherAdjustedChangePercent((decimal?)current, (decimal?)currentDegreeDays, (decimal?)reference, (decimal?)referenceDegreeDays));

    [Fact]
    public void Change_is_missing_without_a_usable_reference()
    {
        Assert.Null(ConsumptionComparison.ChangePercent(100m, 0m));
        Assert.Null(ConsumptionComparison.ChangePercent(100m, null));
        Assert.Null(ConsumptionComparison.ChangePercent(null, 100m));
    }
}
