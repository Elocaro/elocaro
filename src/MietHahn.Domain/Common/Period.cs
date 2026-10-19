using System.Globalization;

namespace MietHahn.Domain.Common;

/// <summary>
/// Gültigkeitszeitraum mit inklusiven Grenzen. <see cref="To"/> = <c>null</c> bedeutet „unbefristet“.
/// Grundlage aller zeitabhängigen Werte (Miete, Flächen, Personen, Verträge) und der taggenauen Abrechnung.
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

    /// <summary>Anzahl Kalendertage inklusive beider Grenzen (nur für befristete Zeiträume).</summary>
    public int Days => To is { } to
        ? to.DayNumber - From.DayNumber + 1
        : throw new InvalidOperationException("Ein unbefristeter Zeitraum hat keine Tagesanzahl.");

    public static Period CalendarYear(int year) => new(new DateOnly(year, 1, 1), new DateOnly(year, 12, 31));

    public static Period Month(int year, int month) =>
        new(new DateOnly(year, month, 1), new DateOnly(year, month, DateTime.DaysInMonth(year, month)));

    public bool Contains(DateOnly day) => day >= From && (To is null || day <= To);

    public bool Overlaps(Period other) =>
        (To is null || other.From <= To) && (other.To is null || From <= other.To);

    /// <summary>Gemeinsamer Teil beider Zeiträume oder <c>null</c>, wenn sie sich nicht überschneiden.</summary>
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

    /// <summary>Tage, die in den Begrenzungszeitraum fallen (z. B. Mietdauer innerhalb eines Abrechnungsjahres).</summary>
    public int DaysIn(Period bounds)
    {
        if (bounds.IsOpenEnded)
        {
            throw new ArgumentException("Die Begrenzung muss befristet sein.", nameof(bounds));
        }

        return Intersect(bounds)?.Days ?? 0;
    }

    /// <summary>Beendet den Zeitraum zum angegebenen Tag (inklusive).</summary>
    public Period EndOn(DateOnly lastDay) => new(From, lastDay);

    /// <summary>Prüft, dass sich keine zwei der Zeiträume überschneiden.</summary>
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
