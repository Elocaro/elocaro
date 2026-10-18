using MietHahn.Domain.Gemeinsam;

namespace MietHahn.Domain.Tests.Gemeinsam;

public class WerktageTests
{
    [Theory]
    [InlineData(2024, 3, 31)]
    [InlineData(2025, 4, 20)]
    [InlineData(2026, 4, 5)]
    [InlineData(2027, 3, 28)]
    [InlineData(2038, 4, 25)]
    public void Ostersonntag(int jahr, int monat, int tag) => Assert.Equal(new DateOnly(jahr, monat, tag), Werktage.Ostersonntag(jahr));

    [Theory]
    [InlineData(2026, 4, 3)] // Karfreitag
    [InlineData(2026, 4, 6)] // Ostermontag
    [InlineData(2026, 5, 14)] // Himmelfahrt
    [InlineData(2026, 5, 25)] // Pfingstmontag
    [InlineData(2026, 10, 3)]
    [InlineData(2026, 12, 26)]
    public void Bundesweite_Feiertage(int jahr, int monat, int tag) => Assert.True(Werktage.IstBundesweiterFeiertag(new DateOnly(jahr, monat, tag)));

    [Theory]
    [InlineData(2026, 10, 5)] // 1.+2.10. Do/Fr, 3.10. Feiertag (Sa), 5.10. Mo
    [InlineData(2026, 1, 6)] // 1.1. Feiertag, 2.1. Fr, 5.1. Mo, 6.1. Di
    [InlineData(2026, 3, 4)] // 2., 3., 4.3.
    [InlineData(2026, 4, 3 + 4)] // 1., 2. April, 3. Karfreitag, 6. Ostermontag → 7.4.
    public void Dritter_Werktag(int jahr, int monat, int tag) => Assert.Equal(new DateOnly(jahr, monat, tag), Werktage.NterWerktag(jahr, monat, 3));

    [Fact]
    public void Samstag_ist_kein_Werktag() => Assert.False(Werktage.IstWerktag(new DateOnly(2026, 10, 17)));
}
