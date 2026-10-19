using MietHahn.Domain.Common;

namespace MietHahn.Domain.Tests.Common;

public class GermanTimeTests
{
    [Theory]
    [InlineData("2026-12-31T22:59:59Z", 2026, 12, 31)] // 23:59:59 CET
    [InlineData("2026-12-31T23:00:00Z", 2027, 1, 1)]   // midnight CET
    [InlineData("2026-06-30T21:59:59Z", 2026, 6, 30)]  // 23:59:59 CEST
    [InlineData("2026-06-30T22:00:00Z", 2026, 7, 1)]   // midnight CEST
    public void Today_refers_to_German_time(string utc, int year, int month, int day)
    {
        var today = GermanTime.Today(DateTimeOffset.Parse(utc, System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(new DateOnly(year, month, day), today);
    }
}
