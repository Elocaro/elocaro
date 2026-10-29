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
    public void Change_is_missing_without_a_usable_reference()
    {
        Assert.Null(ConsumptionComparison.ChangePercent(100m, 0m));
        Assert.Null(ConsumptionComparison.ChangePercent(100m, null));
        Assert.Null(ConsumptionComparison.ChangePercent(null, 100m));
    }
}
