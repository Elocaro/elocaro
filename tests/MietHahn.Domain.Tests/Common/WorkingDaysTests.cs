using MietHahn.Domain.Common;

namespace MietHahn.Domain.Tests.Common;

public class WorkingDaysTests
{
    [Theory]
    [InlineData(2024, 3, 31)]
    [InlineData(2025, 4, 20)]
    [InlineData(2026, 4, 5)]
    [InlineData(2027, 3, 28)]
    [InlineData(2038, 4, 25)]
    public void EasterSunday(int year, int month, int day) => Assert.Equal(new DateOnly(year, month, day), WorkingDays.EasterSunday(year));

    [Theory]
    [InlineData(2026, 4, 3)] // Good Friday
    [InlineData(2026, 4, 6)] // Easter Monday
    [InlineData(2026, 5, 14)] // Ascension Day
    [InlineData(2026, 5, 25)] // Whit Monday
    [InlineData(2026, 10, 3)]
    [InlineData(2026, 12, 26)]
    public void Nationwide_public_holidays(int year, int month, int day) => Assert.True(WorkingDays.IsNationwideHoliday(new DateOnly(year, month, day)));

    [Theory]
    [InlineData(2026, 10, 5)] // 1 and 2 Oct Thu/Fri, 3 Oct holiday (Sat), 5 Oct Mon
    [InlineData(2026, 1, 6)] // 1 Jan holiday, 2 Jan Fri, 5 Jan Mon, 6 Jan Tue
    [InlineData(2026, 3, 4)] // 2, 3, 4 March
    [InlineData(2026, 4, 3 + 4)] // 1 and 2 April, 3 Good Friday, 6 Easter Monday → 7 April
    public void Third_working_day(int year, int month, int day) => Assert.Equal(new DateOnly(year, month, day), WorkingDays.NthWorkingDay(year, month, 3));

    [Fact]
    public void Saturday_is_not_a_working_day() => Assert.False(WorkingDays.IsWorkingDay(new DateOnly(2026, 10, 17)));
}
