using MietHahn.Domain.Common;

namespace MietHahn.Domain.Tests.Common;

public class PeriodTests
{
    private static DateOnly D(int year, int month, int day) => new(year, month, day);

    [Fact]
    public void End_before_start_is_invalid()
    {
        Assert.Throws<ArgumentException>(() => new Period(D(2026, 2, 1), D(2026, 1, 31)));
    }

    [Fact]
    public void Single_day_period_is_valid()
    {
        Assert.Equal(1, new Period(D(2026, 2, 1), D(2026, 2, 1)).Days);
    }

    [Theory]
    [InlineData(2026, 365)]
    [InlineData(2028, 366)]
    public void Calendar_year_has_365_or_366_days(int year, int days)
    {
        Assert.Equal(days, Period.CalendarYear(year).Days);
    }

    [Fact]
    public void Month_ends_on_the_last_day()
    {
        Assert.Equal(new Period(D(2028, 2, 1), D(2028, 2, 29)), Period.Month(2028, 2));
    }

    [Fact]
    public void Open_ended_period_has_no_day_count()
    {
        Assert.Throws<InvalidOperationException>(() => new Period(D(2026, 1, 1)).Days);
    }

    [Theory]
    [InlineData(2026, 1, 1, true)]
    [InlineData(2026, 6, 30, true)]
    [InlineData(2025, 12, 31, false)]
    [InlineData(2026, 7, 1, false)]
    public void Contains_checks_inclusive_bounds(int year, int month, int day, bool expected)
    {
        Assert.Equal(expected, new Period(D(2026, 1, 1), D(2026, 6, 30)).Contains(D(year, month, day)));
    }

    [Fact]
    public void Open_ended_contains_all_later_days()
    {
        Assert.True(new Period(D(2026, 1, 1)).Contains(D(2099, 12, 31)));
    }

    [Theory]
    [InlineData("2026-01-01", "2026-06-30", "2026-06-30", "2026-12-31", true)]  // shared day
    [InlineData("2026-01-01", "2026-06-30", "2026-07-01", "2026-12-31", false)] // directly adjacent
    [InlineData("2026-01-01", null, "2030-01-01", "2030-01-31", true)]          // open-ended
    [InlineData("2026-07-01", null, "2026-01-01", "2026-06-30", false)]
    [InlineData("2026-01-01", null, "2025-01-01", null, true)]
    public void Overlap_is_detected(string from1, string? to1, string from2, string? to2, bool expected)
    {
        var a = Z(from1, to1);
        var b = Z(from2, to2);

        Assert.Equal(expected, a.Overlaps(b));
        Assert.Equal(expected, b.Overlaps(a));
    }

    [Fact]
    public void Intersection_returns_the_common_part()
    {
        var rentalDuration = new Period(D(2025, 6, 15));
        var billingYear = Period.CalendarYear(2025);

        Assert.Equal(new Period(D(2025, 6, 15), D(2025, 12, 31)), rentalDuration.Intersect(billingYear));
        Assert.Null(new Period(D(2024, 1, 1), D(2024, 12, 31)).Intersect(billingYear));
    }

    [Fact]
    public void Intersection_of_two_open_ended_is_open_ended()
    {
        Assert.Equal(new Period(D(2026, 1, 1)), new Period(D(2025, 1, 1)).Intersect(new Period(D(2026, 1, 1))));
    }

    [Fact]
    public void Tenant_change_on_June_15_splits_the_year_to_the_day()
    {
        var year = Period.CalendarYear(2025);
        var old = new Period(D(2020, 1, 1), D(2025, 6, 14));
        var newItem = new Period(D(2025, 6, 15));

        Assert.Equal(165, old.DaysIn(year));
        Assert.Equal(200, newItem.DaysIn(year));
        Assert.Equal(year.Days, old.DaysIn(year) + newItem.DaysIn(year));
    }

    [Fact]
    public void DaysIn_without_overlap_is_zero()
    {
        Assert.Equal(0, new Period(D(2027, 1, 1)).DaysIn(Period.CalendarYear(2026)));
    }

    [Fact]
    public void DaysIn_requires_bounded_period()
    {
        Assert.Throws<ArgumentException>(() => Period.CalendarYear(2026).DaysIn(new Period(D(2026, 1, 1))));
    }

    [Fact]
    public void End_sets_the_end()
    {
        var ended = new Period(D(2026, 1, 1)).EndOn(D(2026, 3, 31));

        Assert.Equal(D(2026, 3, 31), ended.To);
        Assert.Throws<ArgumentException>(() => new Period(D(2026, 1, 1)).EndOn(D(2025, 12, 31)));
    }

    [Fact]
    public void Gapless_sequence_has_no_overlaps()
    {
        Assert.True(Period.AreNonOverlapping([
            Z("2026-07-01", null),
            Z("2026-01-01", "2026-03-31"),
            Z("2026-04-01", "2026-06-30"),
        ]));
    }

    [Fact]
    public void Overlapping_sequence_is_detected()
    {
        Assert.False(Period.AreNonOverlapping([
            Z("2026-01-01", "2026-03-31"),
            Z("2026-03-31", null),
        ]));
    }

    [Fact]
    public void Display_is_German()
    {
        Assert.Equal("01.01.2026 – 31.12.2026", Period.CalendarYear(2026).ToString());
        Assert.Equal("ab 15.06.2025", new Period(D(2025, 6, 15)).ToString());
    }

    private static Period Z(string from, string? to) =>
        new(DateOnly.Parse(from, System.Globalization.CultureInfo.InvariantCulture), to is null ? null : DateOnly.Parse(to, System.Globalization.CultureInfo.InvariantCulture));
}
