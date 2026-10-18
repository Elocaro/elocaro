using MietHahn.Domain.Gemeinsam;

namespace MietHahn.Domain.Tests.Gemeinsam;

public class ZeitzoneTests
{
    [Theory]
    [InlineData("2026-12-31T22:59:59Z", 2026, 12, 31)] // 23:59:59 MEZ
    [InlineData("2026-12-31T23:00:00Z", 2027, 1, 1)]   // Mitternacht MEZ
    [InlineData("2026-06-30T21:59:59Z", 2026, 6, 30)]  // 23:59:59 MESZ
    [InlineData("2026-06-30T22:00:00Z", 2026, 7, 1)]   // Mitternacht MESZ
    public void Heute_bezieht_sich_auf_deutsche_Zeit(string utc, int jahr, int monat, int tag)
    {
        var heute = Zeitzone.Heute(DateTimeOffset.Parse(utc, System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(new DateOnly(jahr, monat, tag), heute);
    }
}
