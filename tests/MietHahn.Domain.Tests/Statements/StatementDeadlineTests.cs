using MietHahn.Domain.Common;
using MietHahn.Domain.Statements;

namespace MietHahn.Domain.Tests.Statements;

public class StatementDeadlineTests
{
    private static readonly Period Year2025 = Period.CalendarYear(2025);

    [Fact]
    public void Deadline_is_twelve_months_after_the_end_of_the_period()
    {
        Assert.Equal(new DateOnly(2026, 12, 31), StatementDeadline.Of(Year2025));
        Assert.Equal(new DateOnly(2027, 6, 30), StatementDeadline.Of(new Period(new DateOnly(2025, 7, 1), new DateOnly(2026, 6, 30))));
        Assert.Equal(new DateOnly(2026, 10, 1), StatementDeadline.ReminderFrom(Year2025));
    }

    [Theory]
    [InlineData(2026, 9, 30, null, 0)]
    [InlineData(2026, 10, 1, DeadlineLevel.Reminder, 91)]
    [InlineData(2026, 11, 18, DeadlineLevel.Reminder, 43)] // more than 6 weeks left
    [InlineData(2026, 11, 19, DeadlineLevel.Notice, 42)]
    [InlineData(2026, 12, 3, DeadlineLevel.Warning, 28)]
    [InlineData(2026, 12, 17, DeadlineLevel.Urgent, 14)]
    [InlineData(2026, 12, 31, DeadlineLevel.Urgent, 0)]
    [InlineData(2027, 1, 1, DeadlineLevel.Expired, 1)]
    public void Level_follows_the_weeks_left(int year, int month, int day, DeadlineLevel? expected, int days)
    {
        var state = StatementDeadline.StateOn(Year2025, new DateOnly(year, month, day));

        Assert.Equal(expected, state?.Level);
        if (expected is not null && days > 0)
        {
            Assert.Equal(days, state!.Days);
        }
    }

    [Fact]
    public void Open_period_has_no_deadline()
    {
        Assert.Throws<RuleViolationException>(() => StatementDeadline.Of(new Period(new DateOnly(2025, 1, 1))));
    }
}
