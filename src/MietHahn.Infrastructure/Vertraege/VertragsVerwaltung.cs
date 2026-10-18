using Microsoft.EntityFrameworkCore;
using MietHahn.Application.Gemeinsam;
using MietHahn.Application.Vertraege;
using MietHahn.Domain.Gemeinsam;
using MietHahn.Domain.Personen;
using MietHahn.Domain.Vertraege;
using MietHahn.Infrastructure.Gemeinsam;
using MietHahn.Infrastructure.Persistenz;

namespace MietHahn.Infrastructure.Vertraege;

internal sealed class VertragsVerwaltung(MietHahnDbContext db, TimeProvider uhr) : IVertragsVerwaltung
{
    private const string Ueberlappung = "Für dieses Mietobjekt gibt es in diesem Zeitraum bereits einen Vertrag.";

    private DateOnly Heute => Zeitzone.Heute(uhr.GetUtcNow());

    public async Task<IReadOnlyList<VertragUebersicht>> ListeAsync(bool nurLaufende = true, CancellationToken cancellationToken = default)
    {
        var heute = Heute;
        var abfrage = MitDetails();
        if (nurLaufende)
        {
            abfrage = abfrage.Where(v => v.Laufzeit.Bis == null || v.Laufzeit.Bis >= heute);
        }

        var vertraege = await abfrage.ToListAsync(cancellationToken);
        var mietobjekte = await Mietobjekt.LadenAsync(db, cancellationToken);
        var namen = await NamenAsync(vertraege.SelectMany(v => v.Parteien.Select(p => p.PersonId)), cancellationToken);
        return vertraege
            .Select(v => new VertragUebersicht(
                v.Id,
                mietobjekte.GetValueOrDefault(v.MietobjektId)?.Bezeichnung ?? "–",
                mietobjekte.GetValueOrDefault(v.MietobjektId)?.Objekt ?? "–",
                MieterText(v, namen),
                v.Beginn,
                v.Ende,
                v.StatusAm(heute),
                v.MonatsmieteAm(v.Beginn > heute ? v.Beginn : heute),
                v.Abrechnungsart))
            .OrderBy(v => v.Objekt, StringComparer.CurrentCulture)
            .ThenBy(v => v.Mietobjekt, StringComparer.CurrentCulture)
            .ThenByDescending(v => v.Beginn)
            .ToList();
    }

    public async Task<VertragDetails?> LadenAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var vertrag = await MitDetails().SingleOrDefaultAsync(v => v.Id == id, cancellationToken);
        if (vertrag is null)
        {
            return null;
        }

        var heute = Heute;
        var stichtag = heute < vertrag.Beginn ? vertrag.Beginn : vertrag.Ende is { } ende && heute > ende ? ende : heute;
        var mietobjekt = (await Mietobjekt.LadenAsync(db, cancellationToken)).GetValueOrDefault(vertrag.MietobjektId);
        var personen = await db.Set<Person>().AsNoTracking().Where(p => vertrag.Parteien.Select(x => x.PersonId).Contains(p.Id)).ToListAsync(cancellationToken);

        return new VertragDetails(
            vertrag.Id,
            vertrag.MietobjektId,
            vertrag.EinheitId,
            vertrag.ZimmerId,
            mietobjekt?.ObjektId ?? Guid.Empty,
            mietobjekt?.Bezeichnung ?? "–",
            mietobjekt?.Objekt ?? "–",
            vertrag.Laufzeit,
            vertrag.BefristetBis,
            vertrag.StatusAm(heute),
            vertrag.KuendigungEingegangenAm,
            vertrag.KuendigungDurch,
            Daten(vertrag),
            vertrag.Parteien.OrderBy(p => p.Rolle).ThenBy(p => p.Reihenfolge).Select(p =>
            {
                var person = personen.Single(x => x.Id == p.PersonId);
                return new VertragsPartei(p.PersonId, person.Anzeigename, p.Rolle, person.Email, person.Telefon ?? person.Mobil);
            }).ToList(),
            vertrag.BestandteileAm(stichtag).Select(Stand).ToList(),
            vertrag.Bestandteile.OrderBy(b => b.Art).ThenByDescending(b => b.Gueltigkeit.Von).Select(Stand).ToList(),
            vertrag.Personen.OrderByDescending(p => p.Gueltigkeit.Von).Select(p => new PersonenStandAnsicht(p.Gueltigkeit, p.Anzahl)).ToList(),
            vertrag.MonatsmieteAm(stichtag),
            stichtag,
            NaechsteFaelligkeit(vertrag, heute),
            vertrag.KuendigungsfristVermieterMonate(heute),
            await MietmodellAsync(vertrag, stichtag, heute, cancellationToken));
    }

    public async Task<IReadOnlyList<MietobjektAuswahl>> MietobjekteAsync(CancellationToken cancellationToken = default)
    {
        var heute = Heute;
        var mietobjekte = await Mietobjekt.LadenAsync(db, cancellationToken);
        var laufende = await MitDetails().Where(v => v.Laufzeit.Von <= heute && (v.Laufzeit.Bis == null || v.Laufzeit.Bis >= heute)).ToListAsync(cancellationToken);
        var namen = await NamenAsync(laufende.SelectMany(v => v.Parteien.Select(p => p.PersonId)), cancellationToken);
        return mietobjekte.Values.Where(m => m.IstVermietbar).Select(m =>
        {
            var vertrag = laufende.FirstOrDefault(v => v.MietobjektId == m.Id);
            return new MietobjektAuswahl(m.Id, m.Bezeichnung, m.Objekt, m.ZimmerId is not null, vertrag is null ? null : new Belegt(MieterText(vertrag, namen), vertrag.Ende));
        }).ToList();
    }

    public async Task<IReadOnlyList<VertragsHistorieEintrag>> HistorieAsync(Guid mietobjektId, CancellationToken cancellationToken = default)
    {
        var vertraege = await MitDetails().Where(v => v.MietobjektId == mietobjektId).ToListAsync(cancellationToken);
        var namen = await NamenAsync(vertraege.SelectMany(v => v.Parteien.Select(p => p.PersonId)), cancellationToken);
        var eintraege = new List<VertragsHistorieEintrag>();
        Mietvertrag? vorheriger = null;
        foreach (var vertrag in vertraege.OrderBy(v => v.Beginn))
        {
            // Lücke zwischen zwei Verträgen = Leerstand
            if (vorheriger?.Ende is { } ende && vertrag.Beginn > ende.AddDays(1))
            {
                eintraege.Add(new(null, new Zeitraum(ende.AddDays(1), vertrag.Beginn.AddDays(-1)), null, null));
            }

            eintraege.Add(new(vertrag.Id, vertrag.Laufzeit, MieterText(vertrag, namen), vertrag.BetragAm(Bestandteilart.Kaltmiete, vertrag.Beginn)));
            vorheriger = vertrag;
        }

        if (vorheriger?.Ende is { } letztesEnde && letztesEnde < Heute)
        {
            eintraege.Add(new(null, new Zeitraum(letztesEnde.AddDays(1)), null, null));
        }

        eintraege.Reverse();
        return eintraege;
    }

    public async Task<Ergebnis> AnlegenAsync(VertragAnlage anlage, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(anlage);
        var mietobjekte = await Mietobjekt.LadenAsync(db, cancellationToken);
        if (anlage.MietobjektId is not { } mietobjektId || !mietobjekte.TryGetValue(mietobjektId, out var mietobjekt))
        {
            return Ergebnis.Fehlgeschlagen("Bitte das Mietobjekt wählen.");
        }

        if (!mietobjekt.IstVermietbar)
        {
            return Ergebnis.Fehlgeschlagen($"{mietobjekt.Bezeichnung} ist eine WG und wird nur zimmerweise vermietet.");
        }

        var parteien = anlage.MieterIds.Distinct().Select(p => (p, Parteirolle.Mieter))
            .Concat(anlage.BuergenIds.Distinct().Except(anlage.MieterIds).Select(p => (p, Parteirolle.Buerge))).ToList();
        if (anlage.MieterIds.Count == 0)
        {
            return Ergebnis.Fehlgeschlagen("Bitte mindestens einen Mieter wählen.");
        }

        var ids = parteien.Select(p => p.p).ToList();
        if (await db.Set<Person>().CountAsync(p => ids.Contains(p.Id) && p.AnonymisiertAm == null, cancellationToken) != ids.Count)
        {
            return Ergebnis.Fehlgeschlagen("Mindestens eine gewählte Person wurde nicht gefunden.");
        }

        return await Befehl.AusfuehrenAsync(async () =>
        {
            var beginn = anlage.Beginn ?? throw new RegelverletzungException("Bitte den Mietbeginn angeben.");
            var vertrag = new Mietvertrag(mietobjekt.Id, mietobjekt.EinheitId, mietobjekt.ZimmerId, beginn, anlage.BefristetBis);
            Uebernehmen(vertrag, anlage.Daten);
            vertrag.Abrechnungsart = anlage.Abrechnungsart;
            vertrag.HeizkostenVerbrauchsabhaengig = anlage.HeizkostenVerbrauchsabhaengig;
            foreach (var (personId, rolle) in parteien)
            {
                vertrag.ParteiHinzufuegen(personId, rolle);
            }

            vertrag.BestandteilSetzen(Bestandteilart.Kaltmiete, beginn, anlage.Kaltmiete ?? throw new RegelverletzungException("Bitte die Kaltmiete angeben."));
            Optional(vertrag, Bestandteilart.BetriebskostenVorauszahlung, beginn, anlage.BetriebskostenVorauszahlung);
            Optional(vertrag, Bestandteilart.HeizkostenVorauszahlung, beginn, anlage.HeizkostenVorauszahlung);
            Optional(vertrag, Bestandteilart.Betriebskostenpauschale, beginn, anlage.Betriebskostenpauschale);
            vertrag.PersonenSetzen(beginn, anlage.Personen);
            PruefeAbrechnungsart(vertrag, beginn);

            db.Add(vertrag);
            await db.SaveChangesAsync(cancellationToken);
            return vertrag.Id;
        }, Ueberlappung);
    }

    public Task<Ergebnis> AendernAsync(Guid id, VertragDaten daten, CancellationToken cancellationToken = default) =>
        MitVertragAsync(id, vertrag =>
        {
            ArgumentNullException.ThrowIfNull(daten);
            Uebernehmen(vertrag, daten);
            vertrag.Abrechnungsart = daten.Abrechnungsart;
            vertrag.HeizkostenVerbrauchsabhaengig = daten.HeizkostenVerbrauchsabhaengig;
        }, cancellationToken);

    public Task<Ergebnis> BestandteilSetzenAsync(Guid id, BestandteilEingabe eingabe, CancellationToken cancellationToken = default) =>
        MitVertragAsync(id, vertrag =>
        {
            ArgumentNullException.ThrowIfNull(eingabe);
            vertrag.BestandteilSetzen(
                eingabe.Art,
                eingabe.Ab ?? throw new RegelverletzungException("Bitte angeben, ab wann der Betrag gilt."),
                eingabe.Betrag ?? throw new RegelverletzungException("Bitte den Betrag angeben."),
                eingabe.Bezeichnung);
        }, cancellationToken);

    public Task<Ergebnis> BestandteilBeendenAsync(Guid id, Bestandteilart art, DateOnly letzterTag, CancellationToken cancellationToken = default) =>
        MitVertragAsync(id, vertrag => vertrag.BestandteilBeenden(art, letzterTag), cancellationToken);

    public Task<Ergebnis> PersonenSetzenAsync(Guid id, PersonenEingabe eingabe, CancellationToken cancellationToken = default) =>
        MitVertragAsync(id, vertrag =>
        {
            ArgumentNullException.ThrowIfNull(eingabe);
            vertrag.PersonenSetzen(eingabe.Ab ?? throw new RegelverletzungException("Bitte angeben, ab wann die Anzahl gilt."), eingabe.Anzahl);
        }, cancellationToken);

    public async Task<Ergebnis> ParteiHinzufuegenAsync(Guid id, Guid personId, Parteirolle rolle, CancellationToken cancellationToken = default)
    {
        if (!await db.Set<Person>().AnyAsync(p => p.Id == personId && p.AnonymisiertAm == null, cancellationToken))
        {
            return Ergebnis.Fehlgeschlagen("Die Person wurde nicht gefunden.");
        }

        return await MitVertragAsync(id, vertrag => vertrag.ParteiHinzufuegen(personId, rolle), cancellationToken);
    }

    public Task<Ergebnis> ParteiEntfernenAsync(Guid id, Guid personId, CancellationToken cancellationToken = default) =>
        MitVertragAsync(id, vertrag => vertrag.ParteiEntfernen(personId), cancellationToken);

    public Task<Ergebnis> KuendigenAsync(Guid id, KuendigungEingabe eingabe, CancellationToken cancellationToken = default) =>
        MitVertragAsync(id, vertrag =>
        {
            ArgumentNullException.ThrowIfNull(eingabe);
            var eingang = eingabe.EingegangenAm ?? throw new RegelverletzungException("Bitte das Eingangsdatum der Kündigung angeben.");
            var zum = eingabe.Zum ?? eingabe.Durch switch
            {
                Kuendigender.Mieter => Mietvertrag.KuendigungsterminFuerMieter(eingang, vertrag.KuendigungsfristMonate),
                Kuendigender.Vermieter => Mietvertrag.KuendigungsterminFuerMieter(eingang, vertrag.KuendigungsfristVermieterMonate(eingang)),
                _ => throw new RegelverletzungException("Bei einer einvernehmlichen Beendigung bitte das Vertragsende angeben."),
            };
            if (vertrag.BefristetBis is { } befristung && zum > befristung)
            {
                zum = befristung;
            }

            vertrag.Kuendigen(eingang, eingabe.Durch, zum);
        }, cancellationToken);

    public Task<Ergebnis> KuendigungAufhebenAsync(Guid id, CancellationToken cancellationToken = default) =>
        MitVertragAsync(id, vertrag => vertrag.KuendigungAufheben(), cancellationToken);

    public async Task<Ergebnis> AuszugAsync(Guid id, AuszugEingabe eingabe, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eingabe);
        var mitAnschrift = !string.IsNullOrWhiteSpace(eingabe.NeueStrasse);
        if (mitAnschrift && (string.IsNullOrWhiteSpace(eingabe.NeuePlz) || string.IsNullOrWhiteSpace(eingabe.NeuerOrt)))
        {
            return Ergebnis.Fehlgeschlagen("Bitte zur neuen Straße auch PLZ und Ort angeben.");
        }

        return await Befehl.AusfuehrenAsync(async () =>
        {
            var vertrag = await MitDetails().SingleOrDefaultAsync(v => v.Id == id, cancellationToken) ?? throw new NichtGefundenException();
            var ende = eingabe.Ende ?? throw new RegelverletzungException("Bitte den letzten Tag des Mietverhältnisses angeben.");
            if (vertrag.KuendigungEingegangenAm is { } eingang && vertrag.KuendigungDurch is { } durch)
            {
                vertrag.Kuendigen(eingang <= ende ? eingang : ende, durch, ende);
            }
            else
            {
                vertrag.Kuendigen(ende, Kuendigender.Einvernehmlich, ende);
            }

            if (mitAnschrift)
            {
                var mieterIds = vertrag.Mieter.Select(m => m.PersonId).ToList();
                var mieter = await db.Set<Person>().Include(p => p.Anschriften).Where(p => mieterIds.Contains(p.Id)).ToListAsync(cancellationToken);
                foreach (var person in mieter)
                {
                    person.AnschriftSetzen(ende.AddDays(1), eingabe.NeueStrasse!, eingabe.NeuePlz!, eingabe.NeuerOrt!);
                }
            }

            await db.SaveChangesAsync(cancellationToken);
            return vertrag.Id;
        }, Ueberlappung);
    }

    public Task<Ergebnis> MietmodellSetzenAsync(Guid id, MietmodellEingabe eingabe, CancellationToken cancellationToken = default) =>
        MitVertragAsync(id, vertrag =>
        {
            ArgumentNullException.ThrowIfNull(eingabe);
            vertrag.MietmodellSetzen(eingabe.Modell, eingabe.IndexBasisMonat, eingabe.IndexBasisWert);
        }, cancellationToken);

    public Task<Ergebnis> StaffelHinzufuegenAsync(Guid id, StaffelEingabe eingabe, CancellationToken cancellationToken = default) =>
        MitVertragAsync(id, vertrag =>
        {
            ArgumentNullException.ThrowIfNull(eingabe);
            vertrag.StaffelHinzufuegen(
                eingabe.Ab ?? throw new RegelverletzungException("Bitte den Termin der Staffel angeben."),
                eingabe.Kaltmiete ?? throw new RegelverletzungException("Bitte die neue Kaltmiete angeben."));
        }, cancellationToken);

    public Task<Ergebnis> StaffelEntfernenAsync(Guid id, Guid staffelId, CancellationToken cancellationToken = default) =>
        MitVertragAsync(id, vertrag => vertrag.StaffelEntfernen(staffelId), cancellationToken);

    public async Task<Ergebnis> IndexAnpassenAsync(Guid id, IndexAnpassungEingabe eingabe, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eingabe);
        var neuester = await db.Set<VpiWert>().AsNoTracking().OrderByDescending(v => v.Monat).FirstOrDefaultAsync(cancellationToken);
        return await MitVertragAsync(id, vertrag =>
        {
            var erklaerung = eingabe.ErklaerungAm ?? throw new RegelverletzungException("Bitte angeben, wann die Erklärung zugeht.");
            if (neuester is null || vertrag.IndexBasisMonat is null || neuester.Monat <= vertrag.IndexBasisMonat)
            {
                throw new RegelverletzungException("Es gibt keinen VPI-Wert, der neuer ist als der Ausgangsindex. Bitte zuerst den VPI pflegen.");
            }

            vertrag.IndexAnpassen(Indexmiete.WirksamAb(erklaerung), neuester.Monat, neuester.Wert);
        }, cancellationToken);
    }

    private async Task<MietmodellAnsicht> MietmodellAsync(Mietvertrag vertrag, DateOnly stichtag, DateOnly heute, CancellationToken cancellationToken)
    {
        var kaltmiete = vertrag.BetragAm(Bestandteilart.Kaltmiete, stichtag);
        var vorDreiJahren = stichtag.AddYears(-3);
        IndexVorschlag? vorschlag = null;
        if (vertrag.Mietmodell == Mietmodell.Index && vertrag.IndexBasisWert is { } basis && vertrag.IndexBasisMonat is { } basisMonat && kaltmiete is { } miete)
        {
            var neuester = await db.Set<VpiWert>().AsNoTracking().Where(v => v.Monat > basisMonat).OrderByDescending(v => v.Monat).FirstOrDefaultAsync(cancellationToken);
            if (neuester is not null)
            {
                var fruehestens = Indexmiete.FruehestesWirksamwerden(vertrag.LetzteKaltmieteAenderung() ?? vertrag.Beginn);
                vorschlag = new IndexVorschlag(
                    neuester.Monat,
                    neuester.Wert,
                    Indexmiete.VeraenderungProzent(basis, neuester.Wert),
                    Indexmiete.NeueMiete(miete, basis, neuester.Wert),
                    fruehestens,
                    Indexmiete.WirksamAb(heute) >= fruehestens);
            }
        }

        return new MietmodellAnsicht(
            vertrag.Mietmodell,
            vertrag.LetzteKaltmieteAenderung(stichtag),
            vorDreiJahren >= vertrag.Beginn ? vertrag.BetragAm(Bestandteilart.Kaltmiete, vorDreiJahren) : null,
            kaltmiete,
            vertrag.IndexBasisMonat,
            vertrag.IndexBasisWert,
            vertrag.Staffeln.OrderBy(s => s.Ab).Select(s => new StaffelAnsicht(s.Id, s.Ab, s.Kaltmiete, s.AngewendetAm)).ToList(),
            vorschlag);
    }

    private static void PruefeAbrechnungsart(Mietvertrag vertrag, DateOnly tag)
    {
        if (vertrag.Abrechnungsart == Abrechnungsart.Pauschale && vertrag.BetragAm(Bestandteilart.Betriebskostenpauschale, tag) is null)
        {
            throw new RegelverletzungException("Bei der Abrechnungsart „Pauschale“ bitte die Betriebskostenpauschale angeben.");
        }
    }

    private static void Optional(Mietvertrag vertrag, Bestandteilart art, DateOnly ab, decimal? betrag)
    {
        if (betrag is > 0)
        {
            vertrag.BestandteilSetzen(art, ab, betrag.Value);
        }
    }

    private static DateOnly? NaechsteFaelligkeit(Mietvertrag vertrag, DateOnly heute)
    {
        var monat = new DateOnly(heute.Year, heute.Month, 1);
        for (var i = 0; i < 24; i++, monat = monat.AddMonths(1))
        {
            var faellig = Werktage.NterWerktag(monat.Year, monat.Month, vertrag.FaelligAmWerktag);
            if (faellig >= heute && faellig >= vertrag.Beginn && (vertrag.Ende is null || monat <= vertrag.Ende))
            {
                return faellig;
            }
        }

        return null;
    }

    private IQueryable<Mietvertrag> MitDetails() =>
        db.Set<Mietvertrag>().Include(v => v.Parteien).Include(v => v.Bestandteile).Include(v => v.Personen).Include(v => v.Staffeln).AsSplitQuery();

    private Task<Ergebnis> MitVertragAsync(Guid id, Action<Mietvertrag> aktion, CancellationToken cancellationToken) =>
        Befehl.AusfuehrenAsync(async () =>
        {
            var vertrag = await MitDetails().SingleOrDefaultAsync(v => v.Id == id, cancellationToken) ?? throw new NichtGefundenException();
            aktion(vertrag);
            await db.SaveChangesAsync(cancellationToken);
            return id;
        }, Ueberlappung);

    private async Task<Dictionary<Guid, string>> NamenAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        var liste = ids.Distinct().ToList();
        var personen = await db.Set<Person>().AsNoTracking().Where(p => liste.Contains(p.Id)).ToListAsync(cancellationToken);
        return personen.ToDictionary(p => p.Id, p => p.Anzeigename);
    }

    internal static string MieterText(Mietvertrag vertrag, IReadOnlyDictionary<Guid, string> namen) =>
        string.Join(", ", vertrag.Mieter.Select(m => namen.GetValueOrDefault(m.PersonId, "?")));

    private static BestandteilStand Stand(Mietbestandteil b) => new(b.Id, b.Art, b.Bezeichnung, b.Gueltigkeit, b.Betrag);

    private static VertragDaten Daten(Mietvertrag v) => new()
    {
        FaelligAmWerktag = v.FaelligAmWerktag,
        Zahlungsart = v.Zahlungsart,
        KuendigungsfristMonate = v.KuendigungsfristMonate,
        Abrechnungsart = v.Abrechnungsart,
        HeizkostenVerbrauchsabhaengig = v.HeizkostenVerbrauchsabhaengig,
        Kaution = v.Kaution,
        KautionNotiz = v.KautionNotiz,
        Notizen = v.Notizen,
    };

    private static void Uebernehmen(Mietvertrag v, VertragDaten d)
    {
        v.FaelligAmWerktag = d.FaelligAmWerktag;
        v.Zahlungsart = d.Zahlungsart;
        v.KuendigungsfristMonate = d.KuendigungsfristMonate;
        v.Kaution = d.Kaution;
        v.KautionNotiz = string.IsNullOrWhiteSpace(d.KautionNotiz) ? null : d.KautionNotiz.Trim();
        v.Notizen = string.IsNullOrWhiteSpace(d.Notizen) ? null : d.Notizen.Trim();
    }
}
