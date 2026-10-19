namespace MietHahn.Domain.Common;

/// <summary>
/// Working days for rental deadlines: Monday to Friday excluding nationwide public holidays. Saturday does not count as a
/// working day for rent and notice periods (BGH VIII ZR 291/09 on rent payment, § 573c BGB applied by analogy).
/// State holidays are not considered (safe side: rather one day earlier).
/// </summary>
public static class WorkingDays
{
    public static bool IsWorkingDay(DateOnly day) =>
        day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !IsNationwideHoliday(day);

    /// <summary>The n-th working day of a month, e.g. rent due on the 3rd working day (§ 556b BGB).</summary>
    public static DateOnly NthWorkingDay(int year, int month, int n)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(n, 1);
        var day = new DateOnly(year, month, 1);
        var counted = 0;
        while (true)
        {
            if (IsWorkingDay(day) && ++counted == n)
            {
                return day;
            }

            day = day.AddDays(1);
        }
    }

    public static bool IsNationwideHoliday(DateOnly day)
    {
        var easter = EasterSunday(day.Year);
        return (day.Month, day.Day) is (1, 1) or (5, 1) or (10, 3) or (12, 25) or (12, 26)
            || day == easter.AddDays(-2) // Good Friday
            || day == easter.AddDays(1) // Easter Monday
            || day == easter.AddDays(39) // Ascension Day
            || day == easter.AddDays(50); // Whit Monday
    }

    /// <summary>Easter Sunday according to the Gauss Easter formula (Gregorian calendar).</summary>
    public static DateOnly EasterSunday(int year)
    {
        var a = year % 19;
        var b = year / 100;
        var c = year % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = ((19 * a) + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + (2 * e) + (2 * i) - h - k) % 7;
        var m = (a + (11 * h) + (22 * l)) / 451;
        var month = (h + l - (7 * m) + 114) / 31;
        var day = ((h + l - (7 * m) + 114) % 31) + 1;
        return new DateOnly(year, month, day);
    }
}
