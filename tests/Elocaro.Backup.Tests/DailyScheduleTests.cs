using Elocaro.Backup;

namespace Elocaro.Backup.Tests;

public class DailyScheduleTests
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    private static TimeSpan Until(string utc, string time) =>
        DailySchedule.Until(DateTimeOffset.Parse(utc, System.Globalization.CultureInfo.InvariantCulture), TimeOnly.Parse(time, System.Globalization.CultureInfo.InvariantCulture), Berlin);

    [Fact]
    public void Later_today() => Assert.Equal(TimeSpan.FromHours(1), Until("2026-03-01T00:30:00Z", "02:30")); // 01:30 CET -> 02:30

    [Fact]
    public void Already_passed_means_tomorrow() => Assert.Equal(TimeSpan.FromHours(23), Until("2026-03-01T03:30:00Z", "03:30"));

    [Fact]
    public void The_hour_skipped_by_daylight_saving_time_runs_an_hour_later() =>
        // 2026-03-29: 02:00 -> 03:00 in Berlin; 02:30 does not exist, so 03:30 CEST = 01:30 UTC
        Assert.Equal(TimeSpan.FromHours(1), Until("2026-03-29T00:30:00Z", "02:30"));

    [Theory]
    [InlineData("02:30", true)]
    [InlineData("23:59", true)]
    [InlineData("2:30pm", false)]
    [InlineData("25:00", false)]
    public void Parses_hours_and_minutes(string text, bool valid) => Assert.Equal(valid, DailySchedule.TryParse(text, out _));
}
