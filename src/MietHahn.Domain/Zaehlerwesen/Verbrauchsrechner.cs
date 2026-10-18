using MietHahn.Domain.Gemeinsam;

namespace MietHahn.Domain.Zaehlerwesen;

/// <summary>Ein Zählerstand zu einem Tag; <see cref="Interpoliert"/> kennzeichnet berechnete Werte.</summary>
public sealed record StandAmTag(DateOnly Datum, decimal Wert, bool Interpoliert);

/// <summary>Verbrauch in einem Zeitraum mit Nachweis der verwendeten Stände.</summary>
/// <param name="Menge">Verbrauch; <c>null</c>, wenn Stände fehlen (siehe <see cref="Fehlt"/>).</param>
public sealed record Verbrauch(Zeitraum Zeitraum, decimal? Menge, IReadOnlyList<StandAmTag> Grundlage, string? Fehlt)
{
    public bool EnthaeltInterpolation => Grundlage.Any(g => g.Interpoliert);
}

/// <summary>
/// Rechenkern für Verbräuche: Stand an einem Stichtag (gemessen oder linear zwischen zwei Ablesungen interpoliert)
/// und Verbrauch über eine Messstelle, auch über einen Zählertausch hinweg. Hochrechnen über die letzte Ablesung hinaus
/// erfolgt nicht; dann fehlt der Wert und muss abgelesen oder geschätzt werden.
/// </summary>
public static class Verbrauchsrechner
{
    /// <summary>Stand am Tag: gemessen, sonst linear interpoliert; <c>null</c> außerhalb der Ablesungen.</summary>
    public static StandAmTag? StandAm(IEnumerable<Zaehlerstand> staende, DateOnly tag)
    {
        ArgumentNullException.ThrowIfNull(staende);
        var liste = staende.OrderBy(s => s.Datum).ToList();
        var exakt = liste.FirstOrDefault(s => s.Datum == tag);
        if (exakt is not null)
        {
            return new StandAmTag(tag, exakt.Wert, false);
        }

        var vorher = liste.LastOrDefault(s => s.Datum < tag);
        var nachher = liste.FirstOrDefault(s => s.Datum > tag);
        if (vorher is null || nachher is null)
        {
            return null;
        }

        var anteil = (decimal)(tag.DayNumber - vorher.Datum.DayNumber) / (nachher.Datum.DayNumber - vorher.Datum.DayNumber);
        return new StandAmTag(tag, Math.Round(vorher.Wert + ((nachher.Wert - vorher.Wert) * anteil), 3, MidpointRounding.AwayFromZero), true);
    }

    /// <summary>
    /// Verbrauch einer Messstelle (Kette getauschter Zähler, älteste zuerst) vom Stand am Ende des Vortags von
    /// <c>zeitraum.Von</c> bis zum Stand am Ende von <c>zeitraum.Bis</c>. Ablesungen gelten als Stand am Ende des Tages.
    /// </summary>
    public static Verbrauch Berechnen(IReadOnlyList<Zaehler> messstelle, Zeitraum zeitraum)
    {
        ArgumentNullException.ThrowIfNull(messstelle);
        if (zeitraum.Bis is not { } bis)
        {
            throw new ArgumentException("Der Zeitraum muss befristet sein.", nameof(zeitraum));
        }

        var start = zeitraum.Von.AddDays(-1);
        var grundlage = new List<StandAmTag>();
        decimal summe = 0;
        foreach (var zaehler in messstelle.OrderBy(z => z.EingebautAm))
        {
            // Abschnitt dieses Zählers innerhalb des Zeitraums
            var von = start > zaehler.EingebautAm ? start : zaehler.EingebautAm;
            var ende = zaehler.AusgebautAm is { } aus && aus < bis ? aus : bis;
            if (ende < von)
            {
                continue;
            }

            var anfang = StandAm(zaehler.Staende, von);
            var schluss = StandAm(zaehler.Staende, ende);
            if (anfang is null || schluss is null)
            {
                var tag = anfang is null ? von : ende;
                return new Verbrauch(zeitraum, null, grundlage, $"Für Zähler {zaehler.Nummer} fehlt ein Stand um den {tag:dd.MM.yyyy} (Ablesung oder Schätzung erfassen).");
            }

            grundlage.Add(anfang);
            grundlage.Add(schluss);
            summe += schluss.Wert - anfang.Wert;
        }

        return grundlage.Count == 0
            ? new Verbrauch(zeitraum, null, grundlage, "Im Zeitraum war kein Zähler eingebaut.")
            : new Verbrauch(zeitraum, summe, grundlage, null);
    }
}
