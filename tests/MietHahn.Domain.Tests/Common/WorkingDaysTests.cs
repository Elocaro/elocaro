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
    [InlineData(2026, 4, 3)] // Karfreitag
    [InlineData(2026, 4, 6)] // Ostermontag
    [InlineData(2026, 5, 14)] // Himmelfahrt
    [InlineData(2026, 5, 25)] // Pfingstmontag
    [InlineData(2026, 10, 3)]
    [InlineData(2026, 12, 26)]
    public void Nationwide_public_holidays(int year, int month, int day) => Assert.True(WorkingDays.IsNationwideHoliday(new DateOnly(year, month, day)));

    [Theory]
    [InlineData(2026, 10, 5)] // 1.+2.10. Do/Fr, 3.10. Feiertag (Sa), 5.10. Mo
    [InlineData(2026, 1, 6)] // 1.1. Feiertag, 2.1. Fr, 5.1. Mo, 6.1. Di
    [InlineData(2026, 3, 4)] // 2., 3., 4.3.
    [InlineData(2026, 4, 3 + 4)] // 1., 2. April, 3. Karfreitag, 6. Ostermontag → 7.4.
    public void Third_working_day(int year, int month, int day) => Assert.Equal(new DateOnly(year, month, day), WorkingDays.NthWorkingDay(year, month, 3));

    [Fact]
    public void Saturday_is_not_a_working_day() => Assert.False(WorkingDays.IsWorkingDay(new DateOnly(2026, 10, 17)));
}
