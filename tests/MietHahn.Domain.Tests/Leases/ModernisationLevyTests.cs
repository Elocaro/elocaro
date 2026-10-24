using MietHahn.Domain.Leases;

namespace MietHahn.Domain.Tests.Leases;

public class ModernisationLevyTests
{
    [Theory]
    [InlineData(5000, 33.33)]
    [InlineData(12000, 80.00)]
    [InlineData(100, 0.67)]
    public void Monthly_increase_is_eight_percent_of_the_costs_a_year_divided_by_twelve(double costs, double expected) =>
        Assert.Equal((decimal)expected, ModernisationLevy.MonthlyIncrease((decimal)costs));

    [Fact]
    public void Costs_minus_maintenance_are_charged_and_the_increase_stays_below_the_cap()
    {
        var result = ModernisationLevy.Assess(500m, 60m, 6000m, 1000m, false, 0m);

        Assert.True(result.IsAllowed);
        Assert.Equal((5000m, 33.33m, 180m), (result.ChargeableCosts, result.AllowedIncrease, result.CapMonthly));
        Assert.False(result.IsCapped);
    }

    [Fact]
    public void The_cap_limits_the_increase_to_three_euros_per_square_metre_in_six_years()
    {
        // 30,000 € → 200 € a month, but only 3 × 50 = 150 € are allowed.
        var result = ModernisationLevy.Assess(500m, 50m, 30000m, 0m, false, 0m);

        Assert.Equal(150m, result.AllowedIncrease);
        Assert.True(result.IsCapped);
        Assert.Contains(result.Warnings, w => w.Contains("Kappungsgrenze", StringComparison.Ordinal));
    }

    [Fact]
    public void A_rent_below_seven_euros_per_square_metre_lowers_the_cap_to_two_euros()
    {
        // 300 € / 50 m² = 6 €/m² → cap 100 €.
        var result = ModernisationLevy.Assess(300m, 50m, 30000m, 0m, false, 0m);

        Assert.Equal((2m, 100m, 100m), (result.CapPerSquareMetre, result.CapMonthly, result.AllowedIncrease));
    }

    [Fact]
    public void Earlier_increases_within_six_years_count_against_the_cap_and_an_exhausted_cap_is_an_error()
    {
        var partly = ModernisationLevy.Assess(500m, 50m, 30000m, 0m, false, 100m);
        var exhausted = ModernisationLevy.Assess(500m, 50m, 30000m, 0m, false, 150m);

        Assert.Equal(50m, partly.AllowedIncrease);
        Assert.False(exhausted.IsAllowed);
    }

    [Fact]
    public void The_simplified_procedure_deducts_thirty_percent_and_is_limited_to_ten_thousand_euros()
    {
        var result = ModernisationLevy.Assess(500m, 60m, 10000m, 0m, true, 0m);
        var tooDear = ModernisationLevy.Assess(500m, 60m, 10001m, 0m, true, 0m);

        Assert.Equal((7000m, 46.67m), (result.ChargeableCosts, result.AllowedIncrease));
        Assert.False(tooDear.IsAllowed);
    }

    [Fact]
    public void Missing_area_costs_or_a_maintenance_share_above_the_costs_are_errors()
    {
        Assert.False(ModernisationLevy.Assess(500m, null, 5000m, 0m, false, 0m).IsAllowed);
        Assert.False(ModernisationLevy.Assess(500m, 60m, 0m, 0m, false, 0m).IsAllowed);
        Assert.False(ModernisationLevy.Assess(500m, 60m, 5000m, 5000m, false, 0m).IsAllowed);
    }

    [Theory]
    [InlineData("2026-03-15", false, "2026-06-01")]
    [InlineData("2026-03-15", true, "2026-12-01")]
    public void Effective_from_is_the_third_month_and_six_months_later_without_proper_announcement(string received, bool extended, string expected) =>
        Assert.Equal(DateOnly.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), ModernisationLevy.EffectiveFrom(DateOnly.Parse(received, System.Globalization.CultureInfo.InvariantCulture), extended));
}
