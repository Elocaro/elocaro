using Elocaro.Domain.Leases;

namespace Elocaro.Domain.Tests.Leases;

public class ComparativeRentTests
{
    [Theory]
    [InlineData("2026-03-15", "2026-06-01", "2026-05-31")]
    [InlineData("2026-03-01", "2026-06-01", "2026-05-31")]
    [InlineData("2026-03-31", "2026-06-01", "2026-05-31")]
    [InlineData("2026-11-20", "2027-02-01", "2027-01-31")]
    [InlineData("2026-12-02", "2027-03-01", "2027-02-28")]
    public void Increase_takes_effect_at_the_start_of_the_third_month_after_receipt_and_consent_ends_before(string received, string effective, string deadline)
    {
        var day = DateOnly.Parse(received, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(DateOnly.Parse(effective, System.Globalization.CultureInfo.InvariantCulture), ComparativeRent.EffectiveFrom(day));
        Assert.Equal(DateOnly.Parse(deadline, System.Globalization.CultureInfo.InvariantCulture), ComparativeRent.ConsentDeadline(day));
    }

    [Theory]
    [InlineData("2025-01-01", true, "2026-01-01")] // one year after the last increase, effective 1 April = 15 months
    [InlineData("2025-01-15", true, "2026-02-01")] // 15 months only on 15 April, so effective 1 May: receipt in February
    [InlineData("2025-01-01", false, "")] // lease start: no lockout, only the 15 months
    public void Earliest_receipt_meets_the_15_months_and_the_lockout_of_one_year(string baseline, bool afterIncrease, string expected)
    {
        var day = DateOnly.Parse(baseline, System.Globalization.CultureInfo.InvariantCulture);

        var earliest = ComparativeRent.EarliestDeclaration(day, afterIncrease);

        Assert.True(ComparativeRent.EffectiveFrom(earliest) >= day.AddMonths(15));
        Assert.True(ComparativeRent.EffectiveFrom(earliest.AddDays(-1)) < day.AddMonths(15) || (afterIncrease && earliest.AddDays(-1) < day.AddMonths(12)));
        if (afterIncrease)
        {
            Assert.Equal(DateOnly.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), earliest);
        }
    }

    [Theory]
    [InlineData(500, 20, 600)]
    [InlineData(500, 15, 575)]
    [InlineData(503.33, 20, 603.99)] // rounded down, never above the limit
    [InlineData(1000, 15, 1150)]
    public void Cap_limit_is_rent_three_years_ago_plus_the_percentage(double rent, int percent, double expected) =>
        Assert.Equal(Dec.From(expected), ComparativeRent.CapLimit(Dec.From(rent), percent));

    [Fact]
    public void Comparative_rent_is_square_metre_price_times_area()
    {
        Assert.Equal(527.77m, ComparativeRent.FromPerSquareMetre(8.90m, 59.3m));
        Assert.Equal(526.50m, ComparativeRent.FromPerSquareMetre(8.775m, 60m));
    }

    [Fact]
    public void Demand_within_all_limits_is_allowed_with_dates_and_maximum()
    {
        var result = ComparativeRent.Assess(new DateOnly(2022, 1, 1), null, 500m, 500m, 20, 580m, new DateOnly(2026, 3, 10), 570m);

        Assert.True(result.IsAllowed);
        Assert.Equal((new DateOnly(2026, 6, 1), new DateOnly(2026, 5, 31), 600m, 580m), (result.EffectiveFrom, result.ConsentDeadline, result.CapLimit, result.MaxRent));
        Assert.Contains("Mietpreisbremse", result.Warnings.Single(), StringComparison.Ordinal);
    }

    [Fact]
    public void Cap_limit_blocks_an_increase_above_20_percent_in_three_years()
    {
        var result = ComparativeRent.Assess(new DateOnly(2020, 1, 1), new DateOnly(2024, 1, 1), 580m, 500m, 20, 900m, new DateOnly(2026, 3, 10), 601m);

        Assert.False(result.IsAllowed);
        Assert.Contains("Kappungsgrenze von 20 %", result.Errors.Single(), StringComparison.Ordinal);
        Assert.Contains("600,00 €", result.Errors.Single(), StringComparison.Ordinal);
    }

    [Fact]
    public void Tight_market_cap_of_15_percent_is_applied()
    {
        var result = ComparativeRent.Assess(new DateOnly(2020, 1, 1), null, 500m, 500m, 15, null, new DateOnly(2026, 3, 10), 580m);

        Assert.False(result.IsAllowed);
        Assert.Equal(575m, result.CapLimit);
        Assert.Equal(575m, result.MaxRent);
    }

    [Fact]
    public void Comparative_rent_is_a_second_limit()
    {
        var result = ComparativeRent.Assess(new DateOnly(2020, 1, 1), null, 500m, 500m, 20, 540m, new DateOnly(2026, 3, 10), 550m);

        Assert.Contains("Vergleichsmiete von 540,00 €", result.Errors.Single(), StringComparison.Ordinal);
        Assert.Equal(540m, result.MaxRent);
    }

    [Fact]
    public void Demand_less_than_a_year_after_the_last_increase_is_refused()
    {
        var result = ComparativeRent.Assess(new DateOnly(2020, 1, 1), new DateOnly(2025, 6, 1), 550m, 500m, 20, null, new DateOnly(2026, 3, 10), 580m);

        Assert.Contains("frühestens ein Jahr nach der letzten Mieterhöhung", result.Errors.Single(), StringComparison.Ordinal);
        Assert.Equal(new DateOnly(2026, 6, 1), result.EarliestDeclaration);
    }

    [Fact]
    public void Rent_must_be_unchanged_for_15_months_when_the_increase_takes_effect()
    {
        // Demand received on 10 Nov 2025 would take effect on 1 Feb 2026 = only 13 months after the start.
        var result = ComparativeRent.Assess(new DateOnly(2025, 1, 1), null, 500m, 500m, 20, null, new DateOnly(2025, 11, 10), 550m);

        Assert.Contains("seit 15 Monaten unverändert", result.Errors.Single(), StringComparison.Ordinal);
        Assert.Equal(new DateOnly(2026, 1, 1), result.EarliestDeclaration);
    }

    [Fact]
    public void Fifteen_months_are_met_when_the_increase_takes_effect_exactly_then()
    {
        var result = ComparativeRent.Assess(new DateOnly(2025, 1, 1), null, 500m, 500m, 20, null, new DateOnly(2026, 1, 1), 550m);

        Assert.True(result.IsAllowed);
        Assert.Equal(new DateOnly(2026, 4, 1), result.EffectiveFrom);
    }

    [Fact]
    public void A_new_rent_that_is_not_higher_is_refused()
    {
        var result = ComparativeRent.Assess(new DateOnly(2020, 1, 1), null, 500m, 500m, 20, null, new DateOnly(2026, 3, 10), 500m);

        Assert.Contains("höher", result.Errors.Single(), StringComparison.Ordinal);
    }
}
