namespace MietHahn.Domain.Common;

/// <summary>A state of a time-dependent value (area, rent component, number of persons ...).</summary>
public interface ITimeDependent
{
    Period Validity { get; }

    /// <summary>Ends the state on the given day (inclusive).</summary>
    void EndValidity(DateOnly lastDay);
}

/// <summary>
/// Maintenance of time-dependent values without overwriting: a new state applies from an effective date, the previous one ends the day before.
/// This keeps earlier values available for statements (e.g. area before a renovation, rent before an increase).
/// </summary>
public static class TimeSeries
{
    /// <summary>
    /// Adds a new open-ended state from <paramref name="from"/>. The latest previous state ends the day before;
    /// if it starts exactly on the effective date, it is replaced (correction). Changes before the latest state are not possible.
    /// </summary>
    /// <returns>The new state.</returns>
    public static T CarryForward<T>(ICollection<T> readings, DateOnly from, Func<Period, T> create)
        where T : class, ITimeDependent
    {
        ArgumentNullException.ThrowIfNull(readings);
        ArgumentNullException.ThrowIfNull(create);

        var latest = readings.MaxBy(s => s.Validity.From);
        if (latest is not null)
        {
            if (from < latest.Validity.From)
            {
                throw new RuleViolationException(
                    $"Der Stichtag muss am oder nach dem {latest.Validity.From:dd.MM.yyyy} liegen (Beginn des letzten Stands). Frühere Stände bleiben unverändert.");
            }

            if (from == latest.Validity.From)
            {
                readings.Remove(latest);
            }
            else if (latest.Validity.Contains(from))
            {
                latest.EndValidity(from.AddDays(-1));
            }
        }

        var newItem = create(new Period(from));
        readings.Add(newItem);
        return newItem;
    }

    /// <summary>The state valid on the day, or <c>null</c>.</summary>
    public static T? At<T>(IEnumerable<T> readings, DateOnly day)
        where T : ITimeDependent =>
        readings.FirstOrDefault(s => s.Validity.Contains(day));
}
