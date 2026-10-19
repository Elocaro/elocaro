namespace MietHahn.Domain.Common;

/// <summary>Business time zone: deadlines, effective dates and date displays refer to German time.</summary>
public static class GermanTime
{
    public static readonly TimeZoneInfo Germany = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    /// <summary>Calendar day in Germany at the given point in time.</summary>
    public static DateOnly Today(DateTimeOffset now) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, Germany).DateTime);
}
