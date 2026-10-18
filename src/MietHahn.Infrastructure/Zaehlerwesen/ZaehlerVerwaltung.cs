using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MietHahn.Application.Aufgaben;
using MietHahn.Application.Gemeinsam;
using MietHahn.Application.Konten;
using MietHahn.Application.Zaehlerwesen;
using MietHahn.Domain.Gemeinsam;
using MietHahn.Domain.Objekte;
using MietHahn.Domain.Zaehlerwesen;
using MietHahn.Infrastructure.Gemeinsam;
using MietHahn.Infrastructure.Persistenz;
using ZaehlerEntitaet = MietHahn.Domain.Zaehlerwesen.Zaehler;

namespace MietHahn.Infrastructure.Zaehlerwesen;

internal sealed class ZaehlerVerwaltung(MietHahnDbContext db, IAktuellerBenutzer benutzer, TimeProvider uhr) : IZaehlerVerwaltung
{
    private DateOnly Heute => Zeitzone.Heute(uhr.GetUtcNow());

    public async Task<IReadOnlyList<ZaehlerZeile>> ListeAsync(Guid objektId, bool mitAusgebauten = false, CancellationToken cancellationToken = default)
    {
        var zaehler = await MitStaenden().Where(z => z.ObjektId == objektId && (mitAusgebauten || z.AusgebautAm == null)).ToListAsync(cancellationToken);
        var namen = await ZuordnungsnamenAsync(objektId, cancellationToken);
        return Sortiert(zaehler).Select(z => Zeile(z, namen)).ToList();
    }

    public async Task<ZaehlerDetails?> LadenAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var zaehler = await MitStaenden().SingleOrDefaultAsync(z => z.Id == id, cancellationToken);
        if (zaehler is null)
        {
            return null;
        }

        var alle = await MitStaenden().Where(z => z.ObjektId == zaehler.ObjektId).ToListAsync(cancellationToken);
        var namen = await ZuordnungsnamenAsync(zaehler.ObjektId, cancellationToken);
        var objekt = await db.Set<Objekt>().AsNoTracking().Where(o => o.Id == zaehler.ObjektId).Select(o => o.Bezeichnung).SingleAsync(cancellationToken);
        var messstelle = Messstelle(zaehler, alle);
        var jahre = Enumerable.Range(Heute.Year - 2, 3).Reverse()
            .Select(j => new Jahresverbrauch(j, Verbrauchsrechner.Berechnen(messstelle, Zeitraum.Kalenderjahr(j))))
            .Where(j => j.Verbrauch.Menge is not null || j.Jahr >= messstelle.Min(z => z.EingebautAm).Year)
            .ToList();
        var unter = alle.Where(z => z.HauptzaehlerId is not null && messstelle.Any(m => m.Id == z.HauptzaehlerId)).ToList();

        return new ZaehlerDetails(
            Zeile(zaehler, namen),
            zaehler.ObjektId,
            objekt,
            Daten(zaehler),
            zaehler.Staende.OrderByDescending(s => s.Datum).Select(Stand).ToList(),
            messstelle.Select(m => Zeile(m, namen)).ToList(),
            jahre,
            Sortiert(unter.Where(u => u.AusgebautAm is null)).Select(u => Zeile(u, namen)).ToList(),
            unter.Count == 0 ? null : Bilanz(Heute.Year - 1, messstelle, unter, alle));
    }

    public async Task<Ergebnis> AnlegenAsync(Guid objektId, ZaehlerAnlage anlage, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(anlage);
        if (!await db.Set<Objekt>().AnyAsync(o => o.Id == objektId, cancellationToken))
        {
            return Befehl.NichtGefunden;
        }

        return await Befehl.AusfuehrenAsync(async () =>
        {
            var daten = anlage.Daten;
            var zaehler = new ZaehlerEntitaet(
                objektId,
                daten.Typ,
                daten.Bezeichnung,
                daten.Nummer,
                anlage.EingebautAm ?? throw new RegelverletzungException("Bitte das Einbaudatum angeben."),
                anlage.Anfangsstand ?? throw new RegelverletzungException("Bitte den Anfangsstand angeben."));
            await UebernehmenAsync(zaehler, daten, cancellationToken);
            if (daten.Reihenfolge == 0)
            {
                zaehler.Reihenfolge = (await db.Set<ZaehlerEntitaet>().Where(z => z.ObjektId == objektId).MaxAsync(z => (int?)z.Reihenfolge, cancellationToken) ?? 0) + 1;
            }

            db.Add(zaehler);
            await db.SaveChangesAsync(cancellationToken);
            return zaehler.Id;
        });
    }

    public Task<Ergebnis> AendernAsync(Guid id, ZaehlerDaten daten, CancellationToken cancellationToken = default) =>
        Befehl.AusfuehrenAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(daten);
            var zaehler = await db.Set<ZaehlerEntitaet>().SingleOrDefaultAsync(z => z.Id == id, cancellationToken) ?? throw new NichtGefundenException();
            if (zaehler.Typ != daten.Typ)
            {
                throw new RegelverletzungException("Der Zählertyp lässt sich nicht ändern. Bitte einen neuen Zähler anlegen.");
            }

            await UebernehmenAsync(zaehler, daten, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return id;
        });

    public async Task<StandErgebnis> StandErfassenAsync(Guid id, StandEingabe eingabe, Guid? fotoDateiId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eingabe);
        var zaehler = await MitStaenden(tracking: true).SingleOrDefaultAsync(z => z.Id == id, cancellationToken);
        if (zaehler is null)
        {
            return new StandErgebnis(false, ["Der Zähler wurde nicht gefunden."], []);
        }

        if (eingabe.Datum is not { } datum || eingabe.Wert is not { } wert)
        {
            return new StandErgebnis(false, ["Bitte Datum und Zählerstand angeben."], []);
        }

        var warnungen = zaehler.Pruefen(datum, wert);
        if (warnungen.Count > 0 && !eingabe.Bestaetigt)
        {
            return new StandErgebnis(false, [], warnungen);
        }

        var ergebnis = await Befehl.AusfuehrenAsync(async () =>
        {
            zaehler.StandErfassen(datum, wert, eingabe.Art, benutzer.Kennung, fotoDateiId, eingabe.Notiz);
            await db.SaveChangesAsync(cancellationToken);
            return id;
        });
        return new StandErgebnis(ergebnis.Erfolgreich, ergebnis.Fehler, warnungen);
    }

    public Task<Ergebnis> StandEntfernenAsync(Guid id, Guid standId, CancellationToken cancellationToken = default) =>
        Befehl.AusfuehrenAsync(async () =>
        {
            var zaehler = await MitStaenden(tracking: true).SingleOrDefaultAsync(z => z.Id == id, cancellationToken) ?? throw new NichtGefundenException();
            zaehler.StandEntfernen(standId);
            await db.SaveChangesAsync(cancellationToken);
            return id;
        });

    public Task<Ergebnis> TauschenAsync(Guid id, TauschEingabe eingabe, CancellationToken cancellationToken = default) =>
        Befehl.AusfuehrenAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(eingabe);
            var zaehler = await MitStaenden(tracking: true).SingleOrDefaultAsync(z => z.Id == id, cancellationToken) ?? throw new NichtGefundenException();
            var neu = zaehler.Tauschen(
                eingabe.Datum ?? throw new RegelverletzungException("Bitte das Tauschdatum angeben."),
                eingabe.Endstand ?? throw new RegelverletzungException("Bitte den Endstand angeben."),
                eingabe.NeueNummer,
                eingabe.Anfangsstand ?? throw new RegelverletzungException("Bitte den Anfangsstand angeben."),
                eingabe.EichfristBis);
            db.Add(neu);

            // Unterzähler hängen ab jetzt am neuen Hauptzähler.
            foreach (var unter in await db.Set<ZaehlerEntitaet>().Where(z => z.HauptzaehlerId == id && z.AusgebautAm == null).ToListAsync(cancellationToken))
            {
                unter.HauptzaehlerId = neu.Id;
            }

            await db.SaveChangesAsync(cancellationToken);
            return neu.Id;
        });

    public async Task<IReadOnlyList<ZaehlerZeile>> RundeAsync(Guid objektId, Guid? einheitId = null, CancellationToken cancellationToken = default)
    {
        var heute = Heute;
        var zaehler = await MitStaenden()
            .Where(z => z.ObjektId == objektId && z.AusgebautAm == null && z.EingebautAm <= heute && (einheitId == null || z.EinheitId == einheitId))
            .ToListAsync(cancellationToken);
        var namen = await ZuordnungsnamenAsync(objektId, cancellationToken);
        return zaehler.OrderBy(z => z.Reihenfolge).ThenBy(z => z.Bezeichnung, StringComparer.CurrentCulture).Select(z => Zeile(z, namen)).ToList();
    }

    public async Task<Verbrauch?> VerbrauchAsync(Guid id, Zeitraum zeitraum, CancellationToken cancellationToken = default)
    {
        var zaehler = await MitStaenden().SingleOrDefaultAsync(z => z.Id == id, cancellationToken);
        if (zaehler is null)
        {
            return null;
        }

        var alle = await MitStaenden().Where(z => z.ObjektId == zaehler.ObjektId && z.Typ == zaehler.Typ).ToListAsync(cancellationToken);
        return Verbrauchsrechner.Berechnen(Messstelle(zaehler, alle), zeitraum);
    }

    /// <summary>Alle Zähler der Messstelle (Kette über Vorgänger/Nachfolger), älteste zuerst.</summary>
    internal static List<ZaehlerEntitaet> Messstelle(ZaehlerEntitaet zaehler, IReadOnlyList<ZaehlerEntitaet> alle)
    {
        var kette = new List<ZaehlerEntitaet> { zaehler };
        for (var vorgaenger = alle.FirstOrDefault(z => z.Id == zaehler.VorgaengerId); vorgaenger is not null; vorgaenger = alle.FirstOrDefault(z => z.Id == vorgaenger.VorgaengerId))
        {
            kette.Insert(0, vorgaenger);
        }

        for (var nachfolger = alle.FirstOrDefault(z => z.VorgaengerId == zaehler.Id); nachfolger is not null; nachfolger = alle.FirstOrDefault(z => z.VorgaengerId == nachfolger.Id))
        {
            kette.Add(nachfolger);
        }

        return kette;
    }

    private static Unterzaehlerbilanz Bilanz(int jahr, List<ZaehlerEntitaet> haupt, List<ZaehlerEntitaet> unter, List<ZaehlerEntitaet> alle)
    {
        var zeitraum = Zeitraum.Kalenderjahr(jahr);
        var hinweise = new List<string>();
        var hauptVerbrauch = Verbrauchsrechner.Berechnen(haupt, zeitraum);
        if (hauptVerbrauch.Fehlt is not null)
        {
            hinweise.Add(hauptVerbrauch.Fehlt);
        }

        decimal summe = 0;
        var vollstaendig = true;
        foreach (var messstelle in unter.Select(u => Messstelle(u, alle)).DistinctBy(m => m[0].Id))
        {
            var verbrauch = Verbrauchsrechner.Berechnen(messstelle, zeitraum);
            if (verbrauch.Menge is { } menge)
            {
                summe += menge;
            }
            else
            {
                vollstaendig = false;
                hinweise.Add(verbrauch.Fehlt!);
            }
        }

        return new Unterzaehlerbilanz(
            jahr,
            hauptVerbrauch.Menge,
            vollstaendig ? summe : null,
            hauptVerbrauch.Menge is { } h && vollstaendig ? h - summe : null,
            hinweise);
    }

    private async Task UebernehmenAsync(ZaehlerEntitaet zaehler, ZaehlerDaten daten, CancellationToken cancellationToken)
    {
        zaehler.Bezeichnung = daten.Bezeichnung.Trim();
        zaehler.Nummer = daten.Nummer.Trim();
        zaehler.Lage = string.IsNullOrWhiteSpace(daten.Lage) ? null : daten.Lage.Trim();
        zaehler.Messeinheit = daten.Messeinheit ?? zaehler.Messeinheit;
        zaehler.EichfristBis = daten.EichfristBis ?? zaehler.EichfristBis;
        zaehler.Notizen = string.IsNullOrWhiteSpace(daten.Notizen) ? null : daten.Notizen.Trim();
        if (daten.Reihenfolge > 0)
        {
            zaehler.Reihenfolge = daten.Reihenfolge;
        }

        // Zuordnung: Einheit oder Zimmer desselben Objekts
        Guid? einheitId = null, zimmerId = null;
        if (daten.ZuordnungId is { } zuordnung)
        {
            var einheit = await db.Set<Einheit>().AsNoTracking().Where(e => e.ObjektId == zaehler.ObjektId && (e.Id == zuordnung || e.Zimmer.Any(z => z.Id == zuordnung)))
                .Select(e => new { e.Id, Zimmer = e.Zimmer.Where(z => z.Id == zuordnung).Select(z => (Guid?)z.Id).FirstOrDefault() })
                .SingleOrDefaultAsync(cancellationToken) ?? throw new RegelverletzungException("Die Zuordnung gehört nicht zu diesem Objekt.");
            einheitId = einheit.Id;
            zimmerId = einheit.Zimmer;
        }

        zaehler.Zuordnen(einheitId, zimmerId);

        if (daten.HauptzaehlerId is { } hauptId)
        {
            var haupt = await db.Set<ZaehlerEntitaet>().AsNoTracking().SingleOrDefaultAsync(z => z.Id == hauptId, cancellationToken);
            if (haupt is null || haupt.ObjektId != zaehler.ObjektId || haupt.Id == zaehler.Id)
            {
                throw new RegelverletzungException("Der Hauptzähler muss ein anderer Zähler desselben Objekts sein.");
            }

            if (haupt.Typ != zaehler.Typ && !(haupt.Typ == Zaehlertyp.Kaltwasser && zaehler.Typ == Zaehlertyp.Warmwasser))
            {
                throw new RegelverletzungException("Haupt- und Unterzähler müssen dasselbe messen (Ausnahme: Warmwasser unter Kaltwasser-Hauptzähler).");
            }
        }

        zaehler.HauptzaehlerId = daten.HauptzaehlerId;
    }

    private IQueryable<ZaehlerEntitaet> MitStaenden(bool tracking = false)
    {
        var abfrage = db.Set<ZaehlerEntitaet>().Include(z => z.Staende);
        return tracking ? abfrage : abfrage.AsNoTracking();
    }

    private async Task<Dictionary<Guid, string>> ZuordnungsnamenAsync(Guid objektId, CancellationToken cancellationToken)
    {
        var einheiten = await db.Set<Einheit>().AsNoTracking().Include(e => e.Zimmer).Where(e => e.ObjektId == objektId).ToListAsync(cancellationToken);
        var namen = einheiten.ToDictionary(e => e.Id, e => e.Bezeichnung);
        foreach (var zimmer in einheiten.SelectMany(e => e.Zimmer.Select(z => (e, z))))
        {
            namen[zimmer.z.Id] = $"{zimmer.e.Bezeichnung}, {zimmer.z.Bezeichnung}";
        }

        return namen;
    }

    private static IEnumerable<ZaehlerEntitaet> Sortiert(IEnumerable<ZaehlerEntitaet> zaehler) =>
        zaehler.OrderBy(z => z.Reihenfolge).ThenBy(z => z.Bezeichnung, StringComparer.CurrentCulture).ThenBy(z => z.EingebautAm);

    private static ZaehlerZeile Zeile(ZaehlerEntitaet z, IReadOnlyDictionary<Guid, string> namen) => new(
        z.Id,
        z.Bezeichnung,
        z.Typ,
        z.Nummer,
        z.Messeinheit,
        z.ZimmerId is { } zimmer ? namen.GetValueOrDefault(zimmer, "?") : z.EinheitId is { } einheit ? namen.GetValueOrDefault(einheit, "?") : "Haus",
        z.EinheitId,
        z.Lage,
        z.Staende.MaxBy(s => s.Datum) is { } letzter ? Stand(letzter) : null,
        z.EichfristBis,
        z.EingebautAm,
        z.AusgebautAm,
        z.HauptzaehlerId,
        z.Reihenfolge);

    private static StandAnsicht Stand(Zaehlerstand s) => new(s.Id, s.Datum, s.Wert, s.Art, s.Ableser, s.FotoDateiId, s.Notiz);

    private static ZaehlerDaten Daten(ZaehlerEntitaet z) => new()
    {
        Typ = z.Typ,
        Messeinheit = z.Messeinheit,
        Bezeichnung = z.Bezeichnung,
        Nummer = z.Nummer,
        Lage = z.Lage,
        ZuordnungId = z.ZimmerId ?? z.EinheitId,
        HauptzaehlerId = z.HauptzaehlerId,
        EichfristBis = z.EichfristBis,
        Reihenfolge = z.Reihenfolge,
        Notizen = z.Notizen,
    };
}

/// <summary>Dashboard: abgelaufene oder bald ablaufende Eichfristen (90 Tage).</summary>
internal sealed class EichfristAufgaben(MietHahnDbContext db, TimeProvider uhr) : IAufgabenQuelle
{
    public async Task<IReadOnlyList<Aufgabe>> AufgabenAsync(ClaimsPrincipal benutzer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(benutzer);
        if (!benutzer.IsInRole(Rollen.Admin) && !benutzer.HasClaim(Rechte.ClaimTyp, nameof(Recht.Stammdaten)))
        {
            return [];
        }

        var heute = Zeitzone.Heute(uhr.GetUtcNow());
        var grenze = heute.AddDays(90);
        var faellig = await db.Set<ZaehlerEntitaet>().AsNoTracking()
            .Where(z => z.AusgebautAm == null && z.EichfristBis != null && z.EichfristBis <= grenze)
            .OrderBy(z => z.EichfristBis)
            .Select(z => new { z.Id, z.Bezeichnung, z.Nummer, z.EichfristBis })
            .ToListAsync(cancellationToken);
        return faellig.Select(z => new Aufgabe(
            "Zähler",
            z.EichfristBis < heute ? $"Eichfrist abgelaufen: {z.Bezeichnung} ({z.Nummer})" : $"Eichfrist läuft ab: {z.Bezeichnung} ({z.Nummer})",
            $"/verwaltung/zaehler/{z.Id}",
            z.EichfristBis < heute ? Dringlichkeit.Dringend : Dringlichkeit.Bald,
            "Nicht geeichte Zähler dürfen nicht für die Abrechnung verwendet werden: Zähler tauschen lassen.",
            z.EichfristBis)).ToList();
    }
}
