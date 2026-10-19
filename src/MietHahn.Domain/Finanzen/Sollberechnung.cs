using MietHahn.Domain.Gemeinsam;
using MietHahn.Domain.Vertraege;

namespace MietHahn.Domain.Finanzen;

public sealed record Monatsposten(Bestandteilart Bestandteil, decimal Betrag, string Bezeichnung, bool Anteilig);

/// <summary>Monatsmieten aus den Mietbestandteilen, bei Ein- und Auszug oder Änderung im Monat taggenau anteilig.</summary>
public static class Sollberechnung
{
    /// <param name="ab">Frühester Tag, für den Miete gefordert wird (Buchhaltungsbeginn).</param>
    public static IReadOnlyList<Monatsposten> Monat(Mietvertrag vertrag, int jahr, int monat, DateOnly? ab = null)
    {
        ArgumentNullException.ThrowIfNull(vertrag);
        var kalendermonat = Zeitraum.Monat(jahr, monat);
        var begrenzung = kalendermonat.Schnitt(vertrag.Laufzeit);
        if (ab is { } beginn)
        {
            begrenzung = begrenzung?.Schnitt(new Zeitraum(beginn));
        }

        if (begrenzung is not { } zeitraum)
        {
            return [];
        }

        var tageImMonat = kalendermonat.Tage;
        var posten = new List<Monatsposten>();
        foreach (var gruppe in vertrag.Bestandteile.GroupBy(b => b.Art).OrderBy(g => g.Key))
        {
            var summe = 0m;
            var tage = 0;
            string? bezeichnung = null;
            foreach (var bestandteil in gruppe.OrderBy(b => b.Gueltigkeit.Von))
            {
                if (bestandteil.Gueltigkeit.Schnitt(zeitraum) is not { } teil)
                {
                    continue;
                }

                // Ungerundet summieren, erst der Monatsbetrag wird auf Cent gerundet.
                summe += bestandteil.Betrag * teil.Tage / tageImMonat;
                tage += teil.Tage;
                bezeichnung = bestandteil.Bezeichnung ?? bezeichnung;
            }

            var betrag = decimal.Round(summe, 2, MidpointRounding.AwayFromZero);
            if (betrag != 0)
            {
                var anteilig = tage < tageImMonat || gruppe.Count(b => b.Gueltigkeit.UeberschneidetSich(zeitraum)) > 1;
                posten.Add(new Monatsposten(gruppe.Key, betrag, Text(gruppe.Key, bezeichnung, jahr, monat, anteilig, tage, tageImMonat), anteilig));
            }
        }

        return posten;
    }

    /// <summary>Monate, für die ab <paramref name="ab"/> bis einschließlich <paramref name="bisMonat"/> Miete anfällt.</summary>
    public static IEnumerable<DateOnly> Monate(Mietvertrag vertrag, DateOnly ab, DateOnly bisMonat)
    {
        ArgumentNullException.ThrowIfNull(vertrag);
        var start = vertrag.Beginn > ab ? vertrag.Beginn : ab;
        var ende = vertrag.Ende is { } e && e < bisMonat ? e : bisMonat;
        for (var monat = new DateOnly(start.Year, start.Month, 1); monat <= ende; monat = monat.AddMonths(1))
        {
            yield return monat;
        }
    }

    public static string Bezeichnungstext(Bestandteilart art) => art switch
    {
        Bestandteilart.Kaltmiete => "Kaltmiete",
        Bestandteilart.BetriebskostenVorauszahlung => "Betriebskostenvorauszahlung",
        Bestandteilart.HeizkostenVorauszahlung => "Heizkostenvorauszahlung",
        Bestandteilart.Betriebskostenpauschale => "Betriebskostenpauschale",
        Bestandteilart.Stellplatz => "Stellplatz",
        Bestandteilart.Moeblierungszuschlag => "Möblierungszuschlag",
        _ => "Sonstiges",
    };

    private static string Text(Bestandteilart art, string? bezeichnung, int jahr, int monat, bool anteilig, int tage, int tageImMonat)
    {
        var name = art == Bestandteilart.Sonstiges && bezeichnung is not null ? bezeichnung : Bezeichnungstext(art);
        var text = $"{name} {monat:00}/{jahr}";
        return anteilig && tage < tageImMonat ? $"{text} (anteilig {tage}/{tageImMonat})" : text;
    }
}
