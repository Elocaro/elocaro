namespace MietHahn.Domain.Gemeinsam;

/// <summary>
/// Werktage für Mietfristen: Montag bis Freitag ohne bundesweite Feiertage. Der Samstag zählt bei Miet- und
/// Kündigungsfristen nicht als Werktag (BGH VIII ZR 291/09 zur Mietzahlung, § 573c BGB analog angewandt).
/// Landesfeiertage sind nicht berücksichtigt (sicherer Fall: eher ein Tag früher).
/// </summary>
public static class Werktage
{
    public static bool IstWerktag(DateOnly tag) =>
        tag.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !IstBundesweiterFeiertag(tag);

    /// <summary>Der n-te Werktag eines Monats, z. B. Fälligkeit der Miete am 3. Werktag (§ 556b BGB).</summary>
    public static DateOnly NterWerktag(int jahr, int monat, int n)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(n, 1);
        var tag = new DateOnly(jahr, monat, 1);
        var gezaehlt = 0;
        while (true)
        {
            if (IstWerktag(tag) && ++gezaehlt == n)
            {
                return tag;
            }

            tag = tag.AddDays(1);
        }
    }

    public static bool IstBundesweiterFeiertag(DateOnly tag)
    {
        var ostern = Ostersonntag(tag.Year);
        return (tag.Month, tag.Day) is (1, 1) or (5, 1) or (10, 3) or (12, 25) or (12, 26)
            || tag == ostern.AddDays(-2) // Karfreitag
            || tag == ostern.AddDays(1) // Ostermontag
            || tag == ostern.AddDays(39) // Christi Himmelfahrt
            || tag == ostern.AddDays(50); // Pfingstmontag
    }

    /// <summary>Ostersonntag nach der Gaußschen Osterformel (gregorianischer Kalender).</summary>
    public static DateOnly Ostersonntag(int jahr)
    {
        var a = jahr % 19;
        var b = jahr / 100;
        var c = jahr % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = ((19 * a) + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + (2 * e) + (2 * i) - h - k) % 7;
        var m = (a + (11 * h) + (22 * l)) / 451;
        var monat = (h + l - (7 * m) + 114) / 31;
        var tag = ((h + l - (7 * m) + 114) % 31) + 1;
        return new DateOnly(jahr, monat, tag);
    }
}
