using Elocaro.Domain.Common;

namespace Elocaro.Domain.Statements;

/// <summary>
/// Statement deadline under § 556 para. 3 BGB: the statement must reach the tenant no later than twelve months after the end
/// of the billing period; afterwards the landlord can no longer claim additional payments (unless he is not responsible for the delay).
/// </summary>
public static class StatementDeadline
{
    /// <summary>Weeks before the deadline at which a warning is raised.</summary>
    public static readonly int[] WarningWeeks = [6, 4, 2];

    public static DateOnly Of(Period billingPeriod)
    {
        var end = billingPeriod.To ?? throw new RuleViolationException("Der Abrechnungszeitraum braucht ein Ende.");
        return end.AddYears(1);
    }

    /// <summary>Day from which the reminder is shown (1 October of the following year for a calendar year).</summary>
    public static DateOnly ReminderFrom(Period billingPeriod)
    {
        var end = billingPeriod.To ?? throw new RuleViolationException("Der Abrechnungszeitraum braucht ein Ende.");
        return end.AddMonths(9).AddDays(1);
    }

    /// <summary>State of the deadline on <paramref name="today"/>; <c>null</c> as long as no reminder is due.</summary>
    public static DeadlineState? StateOn(Period billingPeriod, DateOnly today)
    {
        var deadline = Of(billingPeriod);
        if (today > deadline)
        {
            return new DeadlineState(deadline, DeadlineLevel.Expired, today.DayNumber - deadline.DayNumber);
        }

        if (today < ReminderFrom(billingPeriod))
        {
            return null;
        }

        var daysLeft = deadline.DayNumber - today.DayNumber;
        var level = daysLeft <= WarningWeeks[2] * 7 ? DeadlineLevel.Urgent
            : daysLeft <= WarningWeeks[1] * 7 ? DeadlineLevel.Warning
            : daysLeft <= WarningWeeks[0] * 7 ? DeadlineLevel.Notice
            : DeadlineLevel.Reminder;
        return new DeadlineState(deadline, level, daysLeft);
    }
}

public enum DeadlineLevel
{
    /// <summary>From October on: the statement is due.</summary>
    Reminder = 0,

    /// <summary>Six weeks before.</summary>
    Notice = 1,

    /// <summary>Four weeks before.</summary>
    Warning = 2,

    /// <summary>Two weeks before.</summary>
    Urgent = 3,

    /// <summary>The deadline has passed.</summary>
    Expired = 4,
}

/// <param name="Days">Days left until the deadline; after it, the days since.</param>
public sealed record DeadlineState(DateOnly Deadline, DeadlineLevel Level, int Days);
