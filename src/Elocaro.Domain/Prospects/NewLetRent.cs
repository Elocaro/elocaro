using System.Globalization;

namespace Elocaro.Domain.Prospects;

/// <summary>The check of an asking rent for a new letting against the rent brake and the hints that go with it.</summary>
/// <param name="Limit">Highest net rent per month without an exception (comparative rent + 10 %), or <c>null</c> if it cannot be computed.</param>
/// <param name="OverLimit">The asking rent is above the limit and no previous rent covers it.</param>
public sealed record NewLetCheck(decimal? Limit, bool OverLimit, IReadOnlyList<string> Hints);

/// <summary>
/// Rent brake (§§ 556d ff. BGB) for a new letting in an area with a tight housing market: the net rent may be at most 10 % above the local comparative rent,
/// unless an exception applies (previous rent, first use after 1 October 2014, comprehensive modernisation). Whether the brake applies at all depends on a
/// state ordinance for the municipality; this class cannot know that and says so in its hints. It gives hints, not legal advice.
/// </summary>
public static class NewLetRent
{
    public const decimal Surcharge = 0.10m;

    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    /// <param name="rent">Asking net rent (Kaltmiete) per month.</param>
    /// <param name="area">Living area in m².</param>
    /// <param name="comparativePerSquareMetre">Local comparative rent per m² and month (Mietspiegel), if known.</param>
    /// <param name="previousRent">Net rent the previous tenant paid, if known and to be relied on (§ 556e BGB).</param>
    /// <param name="rentCapPercent">Cap limit of later increases in percent over three years (§ 558 Abs. 3 BGB).</param>
    public static NewLetCheck Check(decimal rent, decimal? area, decimal? comparativePerSquareMetre, decimal? previousRent, int rentCapPercent)
    {
        var hints = new List<string>();
        decimal? limit = null;
        var over = false;
        if (area is not > 0m || comparativePerSquareMetre is not > 0m)
        {
            hints.Add("Die Mietpreisbremse lässt sich nicht prüfen: Am Objekt fehlt die ortsübliche Vergleichsmiete je m² oder bei der Einheit die Wohnfläche.");
        }
        else
        {
            limit = decimal.Round(area.Value * comparativePerSquareMetre.Value * (1 + Surcharge), 2, MidpointRounding.AwayFromZero);
            if (rent <= limit)
            {
                hints.Add($"Die Kaltmiete liegt innerhalb der Grenze von {Money(limit.Value)} (ortsübliche Vergleichsmiete + 10 %, § 556d BGB).");
            }
            else if (previousRent >= rent)
            {
                hints.Add($"Die Kaltmiete liegt über der Grenze von {Money(limit.Value)}, aber nicht über der Vormiete von {Money(previousRent.Value)}. Das ist zulässig (§ 556e BGB), "
                    + "wenn die Vormiete selbst zulässig war und nicht im letzten Jahr vor dem Ende des Vormietverhältnisses vereinbart oder erhöht wurde. Teilen Sie die Vormiete dem Mieter vor Vertragsschluss mit (§ 556g Abs. 1a BGB).");
            }
            else
            {
                over = true;
                hints.Add($"Die Kaltmiete liegt um {Money(rent - limit.Value)} über der Grenze von {Money(limit.Value)} (ortsübliche Vergleichsmiete + 10 %, § 556d BGB). "
                    + "Das ist nur zulässig, wenn eine Ausnahme greift: Vormiete (§ 556e), erste Nutzung nach dem 1. Oktober 2014 oder umfassende Modernisierung (§ 556f). Sonst kann der Mieter nach einer Rüge zu viel gezahlte Miete zurückfordern (§ 556g).");
            }
        }

        hints.Add("Die Mietpreisbremse gilt nur, wenn die Landesregierung sie für Ihre Gemeinde per Verordnung eingeführt hat. Das prüft Elocaro nicht.");
        hints.Add($"Die Kappungsgrenze betrifft spätere Erhöhungen im laufenden Vertrag: höchstens {rentCapPercent} % in drei Jahren bis zur ortsüblichen Vergleichsmiete (§ 558 Abs. 3 BGB).");
        return new NewLetCheck(limit, over, hints);
    }

    private static string Money(decimal value) => value.ToString("N2", German) + " €";
}
