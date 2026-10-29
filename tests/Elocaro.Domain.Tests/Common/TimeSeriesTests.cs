using Elocaro.Domain.Common;

namespace Elocaro.Domain.Tests.Common;

public class TimeSeriesTests
{
    private static DateOnly D(int year, int month, int day) => new(year, month, day);

    [Fact]
    public void First_value_is_open_ended()
    {
        var readings = new List<Entry>();

        TimeSeries.CarryForward(readings, D(2026, 1, 1), g => new Entry(g, 1));

        Assert.Equal(new Period(D(2026, 1, 1)), Assert.Single(readings).Validity);
    }

    [Fact]
    public void New_entry_ends_the_previous_one_the_day_before()
    {
        var readings = new List<Entry>();
        TimeSeries.CarryForward(readings, D(2026, 1, 1), g => new Entry(g, 1));

        TimeSeries.CarryForward(readings, D(2026, 3, 1), g => new Entry(g, 2));

        Assert.Equal([new Period(D(2026, 1, 1), D(2026, 2, 28)), new Period(D(2026, 3, 1))], readings.Select(s => s.Validity));
        Assert.Equal(1, TimeSeries.At(readings, D(2026, 2, 28))!.Value);
        Assert.Equal(2, TimeSeries.At(readings, D(2026, 3, 1))!.Value);
        Assert.Null(TimeSeries.At(readings, D(2025, 12, 31)));
        Assert.True(Period.AreNonOverlapping(readings.Select(s => s.Validity)));
    }

    [Fact]
    public void Same_effective_date_corrects_the_latest_value()
    {
        var readings = new List<Entry>();
        TimeSeries.CarryForward(readings, D(2026, 1, 1), g => new Entry(g, 1));
        TimeSeries.CarryForward(readings, D(2026, 3, 1), g => new Entry(g, 2));

        TimeSeries.CarryForward(readings, D(2026, 3, 1), g => new Entry(g, 3));

        Assert.Equal([1, 3], readings.Select(s => s.Value));
        Assert.Equal(D(2026, 2, 28), readings[0].Validity.To);
    }

    [Fact]
    public void Backdating_before_the_latest_entry_is_not_possible()
    {
        var readings = new List<Entry>();
        TimeSeries.CarryForward(readings, D(2026, 3, 1), g => new Entry(g, 1));

        var errors = Assert.Throws<RuleViolationException>(() => TimeSeries.CarryForward(readings, D(2026, 2, 1), g => new Entry(g, 2)));

        Assert.Contains("01.03.2026", errors.Message, StringComparison.Ordinal);
        Assert.Single(readings);
    }

    [Fact]
    public void After_an_ended_entry_the_gap_remains()
    {
        var readings = new List<Entry>();
        TimeSeries.CarryForward(readings, D(2026, 1, 1), g => new Entry(g, 1)).EndValidity(D(2026, 1, 31));

        TimeSeries.CarryForward(readings, D(2026, 3, 1), g => new Entry(g, 2));

        Assert.Equal(D(2026, 1, 31), readings[0].Validity.To);
        Assert.Null(TimeSeries.At(readings, D(2026, 2, 15)));
    }

    private sealed class Entry(Period validity, int value) : ITimeDependent
    {
        public Period Validity { get; private set; } = validity;

        public int Value { get; } = value;

        public void EndValidity(DateOnly lastDay) => Validity = Validity.EndOn(lastDay);
    }
}
