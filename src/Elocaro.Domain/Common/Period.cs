using System.Globalization;

namespace Elocaro.Domain.Common;

/// <summary>
/// Validity period with inclusive bounds. <see cref="To"/> = <c>null</c> means "open-ended".
/// Basis of all time-dependent values (rent, areas, persons, leases) and of day-exact statements.
/// </summary>
public readonly record struct Period
{
    public Period(DateOnly from, DateOnly? to = null)
    {
        if (to < from)
        {
            throw new ArgumentException($"Das Ende ({to:dd.MM.yyyy}) liegt vor dem Beginn ({from:dd.MM.yyyy}).", nameof(to));
        }

        From = from;
        To = to;
    }

    public DateOnly From { get; init; }

    public DateOnly? To { get; init; }

    public bool IsOpenEnded => To is null;

    /// <summary>Number of calendar days including both bounds (only for bounded periods).</summary>
    public int Days => To is { } to
        ? to.DayNumber - From.DayNumber + 1
        : throw new InvalidOperationException("Ein unbefristeter Zeitraum hat keine Tagesanzahl.");

    public static Period CalendarYear(int year) => new(new DateOnly(year, 1, 1), new DateOnly(year, 12, 31));

    public static Period Month(int year, int month) =>
        new(new DateOnly(year, month, 1), new DateOnly(year, month, DateTime.DaysInMonth(year, month)));

    public bool Contains(DateOnly day) => day >= From && (To is null || day <= To);

    public bool Overlaps(Period other) =>
        (To is null || other.From <= To) && (other.To is null || From <= other.To);

    /// <summary>Common part of both periods, or <c>null</c> if they do not overlap.</summary>
    public Period? Intersect(Period other)
    {
        if (!Overlaps(other))
        {
            return null;
        }

        var from = From > other.From ? From : other.From;
        DateOnly? to = (To, other.To) switch
        {
            (null, null) => null,
            (null, var b) => b,
            (var a, null) => a,
            (var a, var b) => a < b ? a : b,
        };
        return new Period(from, to);
    }

    /// <summary>Days that fall into the bounding period (e.g. rental duration within a billing year).</summary>
    public int DaysIn(Period bounds)
    {
        if (bounds.IsOpenEnded)
        {
            throw new ArgumentException("Die Begrenzung muss befristet sein.", nameof(bounds));
        }

        return Intersect(bounds)?.Days ?? 0;
    }

    /// <summary>Ends the period on the given day (inclusive).</summary>
    public Period EndOn(DateOnly lastDay) => new(From, lastDay);

    /// <summary>Checks that no two of the periods overlap.</summary>
    public static bool AreNonOverlapping(IEnumerable<Period> periods)
    {
        ArgumentNullException.ThrowIfNull(periods);
        var sorted = periods.OrderBy(z => z.From).ToList();
        for (var i = 1; i < sorted.Count; i++)
        {
            if (sorted[i - 1].Overlaps(sorted[i]))
            {
                return false;
            }
        }

        return true;
    }

    public override string ToString() =>
        To is { } to
            ? string.Create(CultureInfo.InvariantCulture, $"{From:dd.MM.yyyy} – {to:dd.MM.yyyy}")
            : string.Create(CultureInfo.InvariantCulture, $"ab {From:dd.MM.yyyy}");
}
