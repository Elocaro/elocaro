namespace MietHahn.Domain.Finanzen;

/// <summary>Eine Forderung (Soll) oder ein Guthaben (Haben) für die Verrechnung, Betrag stets positiv.</summary>
/// <param name="IstMiete">Monatsmiete (für <see cref="Verrechnungsreihenfolge.MieteZuerst"/>).</param>
/// <param name="Bestimmung">Nur bei Guthaben: Forderung, die zuerst getilgt werden soll.</param>
public sealed record Verrechnungsposten(Guid Id, DateOnly Datum, decimal Betrag, Verrechnungsrang Rang = Verrechnungsrang.Hauptforderung, bool IstMiete = false, Guid? Bestimmung = null);

/// <summary>Teil eines Guthabens, der eine Forderung tilgt; <see cref="Datum"/> ist der Tag des Guthabens (Zufluss).</summary>
public sealed record Verrechnungsanteil(Guid GuthabenId, Guid ForderungId, decimal Betrag, DateOnly Datum);

public sealed record Verrechnungsergebnis(IReadOnlyList<Verrechnungsanteil> Anteile, IReadOnlyDictionary<Guid, decimal> Offen, decimal Ueberzahlung)
{
    public decimal Getilgt(Guid forderungId) => Anteile.Where(a => a.ForderungId == forderungId).Sum(a => a.Betrag);
}

/// <summary>
/// Verteilt Zahlungen und Gutschriften auf Forderungen. Ein Guthaben tilgt zuerst die bestimmte Forderung, dann die
/// zu seinem Datum fälligen Forderungen in der gewählten Reihenfolge. Was übrig bleibt (Vorauszahlung, Überzahlung),
/// tilgt spätere Forderungen, sobald sie fällig werden. Forderungen gleichen Rangs und gleicher Fälligkeit (z. B. die
/// Bestandteile einer Monatsmiete) werden anteilig getilgt.
/// </summary>
public static class Verrechnung
{
    public static Verrechnungsergebnis Verteilen(IEnumerable<Verrechnungsposten> forderungen, IEnumerable<Verrechnungsposten> guthaben, Verrechnungsreihenfolge reihenfolge)
    {
        ArgumentNullException.ThrowIfNull(forderungen);
        ArgumentNullException.ThrowIfNull(guthaben);
        var soll = forderungen.ToList();
        var haben = guthaben.ToList();
        if (soll.Concat(haben).Any(p => p.Betrag <= 0))
        {
            throw new ArgumentException("Beträge der Verrechnungsposten müssen positiv sein.");
        }

        var offen = soll.ToDictionary(f => f.Id, f => f.Betrag);
        var rest = haben.ToDictionary(g => g.Id, g => g.Betrag);
        var anteile = new List<Verrechnungsanteil>();
        var faellig = new List<Verrechnungsposten>();
        var pool = new List<Verrechnungsposten>();

        // Zeitlich: am selben Tag werden Forderungen vor den Zahlungen fällig.
        var ereignisse = soll.Select(f => (f.Datum, Art: 0, Posten: f))
            .Concat(haben.Select(g => (g.Datum, Art: 1, Posten: g)))
            .OrderBy(e => e.Datum).ThenBy(e => e.Art).ThenBy(e => e.Posten.Id);

        // Alle Ereignisse eines Tages und einer Art gemeinsam, damit z. B. alle Bestandteile einer Monatsmiete offen sind.
        foreach (var gruppe in ereignisse.GroupBy(e => (e.Datum, e.Art)))
        {
            foreach (var (_, art, posten) in gruppe)
            {
                if (art == 0)
                {
                    faellig.Add(posten);
                }
                else
                {
                    pool.Add(posten);
                    if (posten.Bestimmung is { } bestimmt && offen.TryGetValue(bestimmt, out var offenBestimmt) && offenBestimmt > 0)
                    {
                        Buchen(posten, bestimmt, Math.Min(rest[posten.Id], offenBestimmt));
                    }
                }
            }

            Tilgen();
        }

        return new Verrechnungsergebnis(anteile, offen, rest.Values.Sum());

        void Tilgen()
        {
            while (true)
            {
                var guthaben = pool.FirstOrDefault(g => rest[g.Id] > 0);
                var offene = faellig.Where(f => offen[f.Id] > 0).ToList();
                if (guthaben is null || offene.Count == 0)
                {
                    return;
                }

                var naechste = offene.GroupBy(f => Schluessel(f, reihenfolge)).OrderBy(g => g.Key).First().ToList();
                var betrag = Math.Min(rest[guthaben.Id], naechste.Sum(f => offen[f.Id]));
                foreach (var (forderung, teil) in Anteilig(naechste, betrag))
                {
                    Buchen(guthaben, forderung, teil);
                }
            }
        }

        void Buchen(Verrechnungsposten guthaben, Guid forderung, decimal betrag)
        {
            if (betrag <= 0)
            {
                return;
            }

            rest[guthaben.Id] -= betrag;
            offen[forderung] -= betrag;
            anteile.Add(new Verrechnungsanteil(guthaben.Id, forderung, betrag, guthaben.Datum));
        }

        // Anteilig nach offenen Beträgen, Rundungsreste in Cent an die größten Nachkommareste.
        IEnumerable<(Guid, decimal)> Anteilig(List<Verrechnungsposten> gruppe, decimal betrag)
        {
            var gesamt = gruppe.Sum(f => offen[f.Id]);
            if (betrag == gesamt)
            {
                return gruppe.Select(f => (f.Id, offen[f.Id])).ToList();
            }

            var roh = gruppe.Select(f => (f.Id, Wert: betrag * offen[f.Id] / gesamt)).ToList();
            var abgerundet = roh.Select(r => (r.Id, r.Wert, Cent: decimal.Floor(r.Wert * 100) / 100)).ToList();
            var verbleibend = (int)Math.Round((betrag - abgerundet.Sum(a => a.Cent)) * 100);
            var zuschlag = abgerundet.OrderByDescending(a => a.Wert - a.Cent).ThenBy(a => a.Id).Take(verbleibend).Select(a => a.Id).ToHashSet();
            return abgerundet.Select(a => (a.Id, a.Cent + (zuschlag.Contains(a.Id) ? 0.01m : 0m))).ToList();
        }
    }

    private static (int, DateOnly) Schluessel(Verrechnungsposten f, Verrechnungsreihenfolge reihenfolge) => reihenfolge switch
    {
        Verrechnungsreihenfolge.Gesetzlich => ((int)f.Rang, f.Datum),
        Verrechnungsreihenfolge.MieteZuerst => (f.IstMiete ? 0 : 1, f.Datum),
        _ => (0, f.Datum),
    };
}
