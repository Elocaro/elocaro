using System.Globalization;

namespace Elocaro.Backup;

internal static class DailySchedule
{
    public static bool TryParse(string text, out TimeOnly time) =>
        TimeOnly.TryParseExact(text, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    /// <summary>Time until the next occurrence of a local time of day (the time zone of the container, <c>TZ</c>).</summary>
    public static TimeSpan Until(DateTimeOffset now, TimeOnly time, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(now, zone).DateTime;
        var next = local.Date + time.ToTimeSpan();
        if (next <= local)
        {
            next = next.AddDays(1);
        }

        // The hour skipped by the switch to daylight saving time does not exist: run an hour later.
        if (zone.IsInvalidTime(next))
        {
            next = next.AddHours(1);
        }

        return new DateTimeOffset(next, zone.GetUtcOffset(next)) - now;
    }
}
