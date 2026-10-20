using MietHahn.Domain.Common;
using MietHahn.Domain.Tax;

namespace MietHahn.Domain.Tests.Tax;

public class MaintenanceCalculatorTests
{
    [Fact]
    public void Ten_thousand_over_four_years_gives_2500_per_year()
    {
        var schedule = MaintenanceCalculator.Schedule(10_000m, 2025, 4);

        Assert.Equal([2025, 2026, 2027, 2028], schedule.Select(r => r.Year));
        Assert.All(schedule, r => Assert.Equal(2_500m, r.Amount));
        Assert.Equal([7_500m, 5_000m, 2_500m, 0m], schedule.Select(r => r.Remaining));
    }

    [Fact]
    public void Rounding_remainder_goes_to_the_first_year_and_sum_is_exact()
    {
        var schedule = MaintenanceCalculator.Schedule(1_000m, 2025, 3);

        Assert.Equal([333.34m, 333.33m, 333.33m], schedule.Select(r => r.Amount));
        Assert.Equal(1_000m, schedule.Sum(r => r.Amount));
        Assert.Equal(0m, schedule[^1].Remaining);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    public void Distribution_outside_two_to_five_years_is_rejected(int years) =>
        Assert.Throws<RuleViolationException>(() => new MaintenanceDistribution(Guid.NewGuid(), Guid.NewGuid(), 1_000m, 2025, years));

    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    public void Two_and_five_years_are_allowed(int years) =>
        Assert.Equal(years, new MaintenanceDistribution(Guid.NewGuid(), Guid.NewGuid(), 1_000m, 2025, years).Schedule().Count);
}
