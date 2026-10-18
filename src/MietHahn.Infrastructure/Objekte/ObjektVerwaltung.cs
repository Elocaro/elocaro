using Microsoft.EntityFrameworkCore;
using MietHahn.Application.Gemeinsam;
using MietHahn.Application.Objekte;
using MietHahn.Domain.Dateien;
using MietHahn.Domain.Gemeinsam;
using MietHahn.Domain.Objekte;
using MietHahn.Infrastructure.Persistenz;

namespace MietHahn.Infrastructure.Objekte;

internal sealed class ObjektVerwaltung(MietHahnDbContext db, IBelegungsauskunft belegungen, TimeProvider uhr) : IObjektVerwaltung
{
    private DateOnly Heute => Zeitzone.Heute(uhr.GetUtcNow());

    public async Task<IReadOnlyList<ObjektUebersicht>> ListeAsync(IReadOnlyCollection<Guid>? nurIds = null, CancellationToken cancellationToken = default)
    {
        var abfrage = MitAllem();
        if (nurIds is not null)
        {
            abfrage = abfrage.Where(o => nurIds.Contains(o.Id));
        }

        var objekte = await abfrage.OrderBy(o => o.Bezeichnung).ToListAsync(cancellationToken);
        var mietobjekte = objekte.SelectMany(Mietobjekte).ToList();
        var belegt = await belegungen.AmAsync(mietobjekte.Select(m => m.Id).ToList(), Heute, cancellationToken);
        return objekte.Select(o =>
        {
            var ids = Mietobjekte(o).Select(m => m.Id).ToList();
            var wohnflaeche = o.Einheiten.Select(e => e.FlaecheAm(Heute)?.Wohnflaeche).Where(f => f is not null).Sum();
            return new ObjektUebersicht(o.Id, o.Bezeichnung, o.Anschrift, o.Einheiten.Count, ids.Count, ids.Count(belegt.ContainsKey), wohnflaeche == 0 ? null : wohnflaeche);
        }).ToList();
    }

    public async Task<ObjektDetails?> LadenAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var objekt = await MitAllem().SingleOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (objekt is null)
        {
            return null;
        }

        var belegt = await belegungen.AmAsync(Mietobjekte(objekt).Select(m => m.Id).ToList(), Heute, cancellationToken);
        var einheiten = objekt.Einheiten.OrderBy(e => e.Reihenfolge).Select(e => new EinheitZeile(
            e.Id,
            e.Bezeichnung,
            e.Lage,
            e.Typ,
            e.FlaecheAm(Heute)?.Wohnflaeche,
            e.IstWg,
            e.IstWg
                ? e.Zimmer.OrderBy(z => z.Reihenfolge).Select(z => new MietobjektZeile(z.Id, z.Bezeichnung, true, z.FlaecheAm(Heute)?.Flaeche, belegt.GetValueOrDefault(z.Id))).ToList()
                : [new MietobjektZeile(e.Id, e.Bezeichnung, false, e.FlaecheAm(Heute)?.Wohnflaeche, belegt.GetValueOrDefault(e.Id))])).ToList();

        return new ObjektDetails(
            objekt.Id,
            Daten(objekt),
            objekt.Anschrift,
            objekt.BildDateiId,
            Stand(objekt.FlaecheAm(Heute)),
            Historie(objekt.Flaechen, f => new FlaechenStand(f.Gueltigkeit, f.Gesamtflaeche, f.Gemeinschaftsflaeche)),
            einheiten,
            objekt.Pruefhinweise(Heute));
    }

    public Task<Ergebnis> AnlegenAsync(ObjektDaten daten, CancellationToken cancellationToken = default) =>
        AusfuehrenAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(daten);
            var objekt = new Objekt(daten.Bezeichnung.Trim(), daten.Strasse.Trim(), daten.Plz.Trim(), daten.Ort.Trim());
            Uebernehmen(objekt, daten);
            db.Add(objekt);
            await db.SaveChangesAsync(cancellationToken);
            return objekt.Id;
        });

    public Task<Ergebnis> AendernAsync(Guid id, ObjektDaten daten, CancellationToken cancellationToken = default) =>
        MitObjektAsync(id, objekt =>
        {
            ArgumentNullException.ThrowIfNull(daten);
            Uebernehmen(objekt, daten);
        }, cancellationToken);

    public async Task<Ergebnis> BildSetzenAsync(Guid id, Guid? dateiId, CancellationToken cancellationToken = default)
    {
        if (dateiId is { } datei && !await db.Set<Datei>().AnyAsync(d => d.Id == datei && d.MimeTyp.StartsWith("image/"), cancellationToken))
        {
            return Ergebnis.Fehlgeschlagen("Das Foto muss ein Bild sein.");
        }

        return await MitObjektAsync(id, objekt => objekt.BildDateiId = dateiId, cancellationToken);
    }

    public Task<Ergebnis> FlaecheSetzenAsync(Guid id, FlaechenEingabe eingabe, CancellationToken cancellationToken = default) =>
        MitObjektAsync(id, objekt => objekt.FlaecheSetzen(Ab(eingabe), Pflicht(eingabe.Flaeche), eingabe.Zusatz ?? 0), cancellationToken);

    public async Task<Ergebnis> LoeschenAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var objekt = await MitAllem().SingleOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (objekt is null)
        {
            return NichtGefunden;
        }

        if (objekt.Einheiten.Count > 0)
        {
            return Ergebnis.Fehlgeschlagen("Das Objekt hat noch Einheiten. Bitte zuerst die Einheiten löschen.");
        }

        db.RemoveRange(await db.Set<ObjektZuordnung>().Where(z => z.ObjektId == id).ToListAsync(cancellationToken));
        db.Remove(objekt);
        await db.SaveChangesAsync(cancellationToken);
        return Ergebnis.Ok(id);
    }

    public async Task<Ergebnis> EinheitAnlegenAsync(Guid objektId, EinheitDaten daten, CancellationToken cancellationToken = default)
    {
        Guid? neu = null;
        var ergebnis = await MitObjektAsync(objektId, objekt =>
        {
            ArgumentNullException.ThrowIfNull(daten);
            var einheit = objekt.EinheitHinzufuegen(daten.Bezeichnung, daten.Typ);
            Uebernehmen(einheit, daten);
            neu = einheit.Id;
        }, cancellationToken);
        return ergebnis.Erfolgreich ? Ergebnis.Ok(neu) : ergebnis;
    }

    public async Task<EinheitDetails?> EinheitLadenAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var objekt = await MitAllem().SingleOrDefaultAsync(o => o.Einheiten.Any(e => e.Id == id), cancellationToken);
        var einheit = objekt?.Einheiten.Single(e => e.Id == id);
        if (objekt is null || einheit is null)
        {
            return null;
        }

        var belegt = await belegungen.AmAsync([einheit.Id, .. einheit.Zimmer.Select(z => z.Id)], Heute, cancellationToken);
        return new EinheitDetails(
            einheit.Id,
            objekt.Id,
            objekt.Bezeichnung,
            Daten(einheit),
            Stand(einheit.FlaecheAm(Heute)),
            Historie(einheit.Flaechen, f => new FlaechenStand(f.Gueltigkeit, f.Wohnflaeche, f.Heizflaeche)),
            einheit.Zimmer.OrderBy(z => z.Reihenfolge).Select(z => new ZimmerZeile(z.Id, z.Bezeichnung, z.Moeblierung, Stand(z.FlaecheAm(Heute)), belegt.GetValueOrDefault(z.Id))).ToList(),
            belegt.GetValueOrDefault(einheit.Id),
            einheit.Pruefhinweise(Heute).ToList());
    }

    public Task<Ergebnis> EinheitAendernAsync(Guid id, EinheitDaten daten, CancellationToken cancellationToken = default) =>
        MitEinheitAsync(id, (objekt, einheit) =>
        {
            ArgumentNullException.ThrowIfNull(daten);
            if (objekt.Einheiten.Any(e => e.Id != id && string.Equals(e.Bezeichnung, daten.Bezeichnung.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                throw new RegelverletzungException($"Im Objekt gibt es bereits eine Einheit „{daten.Bezeichnung.Trim()}“.");
            }

            Uebernehmen(einheit, daten);
        }, cancellationToken);

    public Task<Ergebnis> EinheitFlaecheSetzenAsync(Guid id, FlaechenEingabe eingabe, CancellationToken cancellationToken = default) =>
        MitEinheitAsync(id, (_, einheit) => einheit.FlaecheSetzen(Ab(eingabe), Pflicht(eingabe.Flaeche), eingabe.Zusatz), cancellationToken);

    public async Task<Ergebnis> EinheitLoeschenAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (await HatVertraegeAsync(id, cancellationToken))
        {
            return Ergebnis.Fehlgeschlagen("Für die Einheit gibt es Mietverträge. Sie kann nicht gelöscht werden.");
        }

        return await MitEinheitAsync(id, (objekt, einheit) =>
        {
            if (einheit.IstWg)
            {
                throw new RegelverletzungException("Die Einheit hat noch Zimmer. Bitte zuerst die Zimmer löschen.");
            }

            objekt.Einheiten.Remove(einheit);
        }, cancellationToken);
    }

    public async Task<Ergebnis> ZimmerAnlegenAsync(Guid einheitId, ZimmerDaten daten, CancellationToken cancellationToken = default)
    {
        if (await HatVertraegeAsync(einheitId, cancellationToken))
        {
            return Ergebnis.Fehlgeschlagen("Die Einheit ist als Ganzes vermietet. Zimmer sind nur für WGs ohne Vertrag über die ganze Einheit möglich.");
        }

        Guid? neu = null;
        var ergebnis = await MitEinheitAsync(einheitId, (_, einheit) =>
        {
            ArgumentNullException.ThrowIfNull(daten);
            var zimmer = einheit.ZimmerHinzufuegen(daten.Bezeichnung);
            Uebernehmen(zimmer, daten);
            neu = zimmer.Id;
        }, cancellationToken);
        return ergebnis.Erfolgreich ? Ergebnis.Ok(neu) : ergebnis;
    }

    public async Task<ZimmerDetails?> ZimmerLadenAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var objekt = await MitAllem().SingleOrDefaultAsync(o => o.Einheiten.Any(e => e.Zimmer.Any(z => z.Id == id)), cancellationToken);
        var einheit = objekt?.Einheiten.Single(e => e.Zimmer.Any(z => z.Id == id));
        var zimmer = einheit?.Zimmer.Single(z => z.Id == id);
        if (objekt is null || einheit is null || zimmer is null)
        {
            return null;
        }

        var belegt = await belegungen.AmAsync([zimmer.Id], Heute, cancellationToken);
        return new ZimmerDetails(
            zimmer.Id,
            einheit.Id,
            einheit.Bezeichnung,
            objekt.Id,
            objekt.Bezeichnung,
            new ZimmerDaten { Bezeichnung = zimmer.Bezeichnung, Moeblierung = zimmer.Moeblierung, Notizen = zimmer.Notizen },
            Stand(zimmer.FlaecheAm(Heute)),
            Historie(zimmer.Flaechen, f => new FlaechenStand(f.Gueltigkeit, f.Flaeche, f.AnteilGemeinschaftsflaeche)),
            belegt.GetValueOrDefault(zimmer.Id));
    }

    public Task<Ergebnis> ZimmerAendernAsync(Guid id, ZimmerDaten daten, CancellationToken cancellationToken = default) =>
        MitZimmerAsync(id, (einheit, zimmer) =>
        {
            ArgumentNullException.ThrowIfNull(daten);
            if (einheit.Zimmer.Any(z => z.Id != id && string.Equals(z.Bezeichnung, daten.Bezeichnung.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                throw new RegelverletzungException($"In der Einheit gibt es bereits ein Zimmer „{daten.Bezeichnung.Trim()}“.");
            }

            Uebernehmen(zimmer, daten);
        }, cancellationToken);

    public Task<Ergebnis> ZimmerFlaecheSetzenAsync(Guid id, FlaechenEingabe eingabe, CancellationToken cancellationToken = default) =>
        MitZimmerAsync(id, (_, zimmer) => zimmer.FlaecheSetzen(Ab(eingabe), Pflicht(eingabe.Flaeche), eingabe.Zusatz ?? 0), cancellationToken);

    public async Task<Ergebnis> ZimmerLoeschenAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (await HatVertraegeAsync(id, cancellationToken))
        {
            return Ergebnis.Fehlgeschlagen("Für das Zimmer gibt es Mietverträge. Es kann nicht gelöscht werden.");
        }

        return await MitZimmerAsync(id, (einheit, zimmer) =>
        {
            einheit.Zimmer.Remove(zimmer);
        }, cancellationToken);
    }

    private Task<bool> HatVertraegeAsync(Guid mietobjektId, CancellationToken cancellationToken) =>
        belegungen.HatVertraegeAsync(mietobjektId, cancellationToken);

    private IQueryable<Objekt> MitAllem() =>
        db.Set<Objekt>()
            .Include(o => o.Flaechen)
            .Include(o => o.Einheiten).ThenInclude(e => e.Flaechen)
            .Include(o => o.Einheiten).ThenInclude(e => e.Zimmer).ThenInclude(z => z.Flaechen)
            .AsSplitQuery();

    private static IEnumerable<(Guid Id, string Bezeichnung)> Mietobjekte(Objekt objekt) =>
        objekt.Einheiten.SelectMany(e => e.IstWg ? e.Zimmer.Select(z => (z.Id, z.Bezeichnung)) : [(e.Id, e.Bezeichnung)]);

    private Task<Ergebnis> MitObjektAsync(Guid id, Action<Objekt> aktion, CancellationToken cancellationToken) =>
        AusfuehrenAsync(async () =>
        {
            var objekt = await MitAllem().SingleOrDefaultAsync(o => o.Id == id, cancellationToken) ?? throw new NichtGefundenException();
            aktion(objekt);
            objekt.Pruefen();
            await db.SaveChangesAsync(cancellationToken);
            return id;
        });

    private Task<Ergebnis> MitEinheitAsync(Guid id, Action<Objekt, Einheit> aktion, CancellationToken cancellationToken) =>
        AusfuehrenAsync(async () =>
        {
            var objekt = await MitAllem().SingleOrDefaultAsync(o => o.Einheiten.Any(e => e.Id == id), cancellationToken) ?? throw new NichtGefundenException();
            aktion(objekt, objekt.Einheiten.Single(e => e.Id == id));
            await db.SaveChangesAsync(cancellationToken);
            return id;
        });

    private Task<Ergebnis> MitZimmerAsync(Guid id, Action<Einheit, Zimmer> aktion, CancellationToken cancellationToken) =>
        AusfuehrenAsync(async () =>
        {
            var objekt = await MitAllem().SingleOrDefaultAsync(o => o.Einheiten.Any(e => e.Zimmer.Any(z => z.Id == id)), cancellationToken) ?? throw new NichtGefundenException();
            var einheit = objekt.Einheiten.Single(e => e.Zimmer.Any(z => z.Id == id));
            aktion(einheit, einheit.Zimmer.Single(z => z.Id == id));
            await db.SaveChangesAsync(cancellationToken);
            return id;
        });

    private static async Task<Ergebnis> AusfuehrenAsync(Func<Task<Guid>> befehl)
    {
        try
        {
            return Ergebnis.Ok(await befehl());
        }
        catch (NichtGefundenException)
        {
            return NichtGefunden;
        }
        catch (RegelverletzungException fehler)
        {
            return Ergebnis.Fehlgeschlagen(fehler.Message);
        }
        catch (Exception fehler) when (Datenbankfehler.IstUeberlappung(fehler))
        {
            return Ergebnis.Fehlgeschlagen("Für diesen Zeitraum gibt es bereits einen Stand.");
        }
        catch (DbUpdateConcurrencyException)
        {
            return Ergebnis.Fehlgeschlagen("Der Datensatz wurde zwischenzeitlich geändert. Bitte die Seite neu laden.");
        }
    }

    private static Ergebnis NichtGefunden => Ergebnis.Fehlgeschlagen("Der Datensatz wurde nicht gefunden.");

    private static DateOnly Ab(FlaechenEingabe eingabe) =>
        eingabe.Ab ?? throw new RegelverletzungException("Bitte angeben, ab wann die Fläche gilt.");

    private static decimal Pflicht(decimal? wert) => wert ?? throw new RegelverletzungException("Bitte die Fläche angeben.");

    private static FlaechenStand? Stand(ObjektFlaeche? f) => f is null ? null : new(f.Gueltigkeit, f.Gesamtflaeche, f.Gemeinschaftsflaeche);

    private static FlaechenStand? Stand(EinheitFlaeche? f) => f is null ? null : new(f.Gueltigkeit, f.Wohnflaeche, f.Heizflaeche);

    private static FlaechenStand? Stand(ZimmerFlaeche? f) => f is null ? null : new(f.Gueltigkeit, f.Flaeche, f.AnteilGemeinschaftsflaeche);

    private static List<FlaechenStand> Historie<T>(IEnumerable<T> staende, Func<T, FlaechenStand> abbilden)
        where T : IZeitabhaengig =>
        staende.OrderByDescending(s => s.Gueltigkeit.Von).Select(abbilden).ToList();

    private static ObjektDaten Daten(Objekt o) => new()
    {
        Bezeichnung = o.Bezeichnung,
        Strasse = o.Strasse,
        Plz = o.Plz,
        Ort = o.Ort,
        Baujahr = o.Baujahr,
        Gemarkung = o.Gemarkung,
        Flurstueck = o.Flurstueck,
        GrundsteuerAktenzeichen = o.GrundsteuerAktenzeichen,
        Heizungsart = o.Heizungsart,
        Energietraeger = o.Energietraeger,
        Notizen = o.Notizen,
    };

    private static EinheitDaten Daten(Einheit e) => new()
    {
        Bezeichnung = e.Bezeichnung,
        Lage = e.Lage,
        Typ = e.Typ,
        Zimmeranzahl = e.Zimmeranzahl,
        Bad = e.Bad,
        Balkon = e.Balkon,
        Einbaukueche = e.Einbaukueche,
        Energiestandard = e.Energiestandard,
        Ausstattung = e.Ausstattung,
    };

    private static void Uebernehmen(Objekt o, ObjektDaten d)
    {
        o.Bezeichnung = d.Bezeichnung.Trim();
        o.Strasse = d.Strasse.Trim();
        o.Plz = d.Plz.Trim();
        o.Ort = d.Ort.Trim();
        o.Baujahr = d.Baujahr;
        o.Gemarkung = Leer(d.Gemarkung);
        o.Flurstueck = Leer(d.Flurstueck);
        o.GrundsteuerAktenzeichen = Leer(d.GrundsteuerAktenzeichen);
        o.Heizungsart = d.Heizungsart;
        o.Energietraeger = d.Energietraeger;
        o.Notizen = Leer(d.Notizen);
    }

    private static void Uebernehmen(Einheit e, EinheitDaten d)
    {
        e.Bezeichnung = d.Bezeichnung.Trim();
        e.Lage = Leer(d.Lage);
        e.Typ = d.Typ;
        e.Zimmeranzahl = d.Zimmeranzahl;
        e.Bad = Leer(d.Bad);
        e.Balkon = d.Balkon;
        e.Einbaukueche = d.Einbaukueche;
        e.Energiestandard = Leer(d.Energiestandard);
        e.Ausstattung = Leer(d.Ausstattung);
    }

    private static void Uebernehmen(Zimmer z, ZimmerDaten d)
    {
        z.Bezeichnung = d.Bezeichnung.Trim();
        z.Moeblierung = d.Moeblierung;
        z.Notizen = Leer(d.Notizen);
    }

    private static string? Leer(string? wert) => string.IsNullOrWhiteSpace(wert) ? null : wert.Trim();

    private sealed class NichtGefundenException : Exception;
}

/// <summary>Ohne Vertragsmodul: alles steht leer.</summary>
internal sealed class KeineBelegung : IBelegungsauskunft
{
    public Task<IReadOnlyDictionary<Guid, Belegung>> AmAsync(IReadOnlyCollection<Guid> mietobjektIds, DateOnly tag, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, Belegung>>(new Dictionary<Guid, Belegung>());

    public Task<bool> HatVertraegeAsync(Guid mietobjektId, CancellationToken cancellationToken = default) => Task.FromResult(false);
}
