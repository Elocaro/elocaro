using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MietHahn.Application.Aufgaben;
using MietHahn.Application.Dateien;
using MietHahn.Application.Dokumente;
using MietHahn.Application.Gemeinsam;
using MietHahn.Application.Konten;
using MietHahn.Application.Personen;
using MietHahn.Domain.Dateien;
using MietHahn.Domain.Dokumente;
using MietHahn.Domain.Gemeinsam;
using MietHahn.Domain.Objekte;
using MietHahn.Domain.Personen;
using MietHahn.Domain.Vertraege;
using MietHahn.Infrastructure.Gemeinsam;
using MietHahn.Infrastructure.Persistenz;
using MietHahn.Infrastructure.Vertraege;

namespace MietHahn.Infrastructure.Dokumente;

internal sealed class Dokumentablage(MietHahnDbContext db, TimeProvider uhr) : IDokumentablage
{
    public async Task<IReadOnlyList<DokumentZeile>> SucheAsync(DokumentSuche suche, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(suche);
        var abfrage = db.Set<Dokument>().AsNoTracking();
        if (suche.Kategorie is { } kategorie)
        {
            abfrage = abfrage.Where(d => d.Kategorie == kategorie);
        }

        if (suche.ObjektId is { } objekt)
        {
            // Zum Objekt gehören auch Dokumente seiner Einheiten und Verträge.
            var einheiten = db.Set<Einheit>().Where(e => e.ObjektId == objekt).Select(e => e.Id);
            var vertraege = db.Set<Mietvertrag>().Where(v => einheiten.Contains(v.EinheitId)).Select(v => v.Id);
            abfrage = abfrage.Where(d => d.ObjektId == objekt || (d.EinheitId != null && einheiten.Contains(d.EinheitId.Value)) || (d.MietvertragId != null && vertraege.Contains(d.MietvertragId.Value)));
        }

        if (suche.EinheitId is { } einheit)
        {
            var vertraege = db.Set<Mietvertrag>().Where(v => v.EinheitId == einheit).Select(v => v.Id);
            abfrage = abfrage.Where(d => d.EinheitId == einheit || (d.MietvertragId != null && vertraege.Contains(d.MietvertragId.Value)));
        }

        if (suche.MietvertragId is { } vertrag)
        {
            abfrage = abfrage.Where(d => d.MietvertragId == vertrag);
        }

        if (suche.PersonId is { } person)
        {
            var vertraege = db.Set<Vertragspartei>().Where(p => p.PersonId == person).Select(p => p.MietvertragId);
            abfrage = abfrage.Where(d => d.PersonId == person || (d.MietvertragId != null && vertraege.Contains(d.MietvertragId.Value)));
        }

        if (!string.IsNullOrWhiteSpace(suche.Text))
        {
            var muster = "%" + suche.Text.Trim().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
            var dateien = db.Set<Datei>().Where(f => EF.Functions.ILike(f.Name, muster)).Select(f => f.Id);
            abfrage = abfrage.Where(d => EF.Functions.ILike(d.Titel, muster)
                || d.Tags.Any(t => EF.Functions.ILike(t, muster))
                || (d.Notiz != null && EF.Functions.ILike(d.Notiz, muster))
                || dateien.Contains(d.DateiId));
        }

        var dokumente = await abfrage.OrderByDescending(d => d.Datum ?? DateOnly.MinValue).ThenByDescending(d => d.ErstelltAm).ToListAsync(cancellationToken);
        return await ZeilenAsync(dokumente, cancellationToken);
    }

    public async Task<DokumentDetails?> LadenAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var dokument = await db.Set<Dokument>().AsNoTracking().SingleOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (dokument is null)
        {
            return null;
        }

        var datei = await db.Set<Datei>().AsNoTracking().SingleAsync(f => f.Id == dokument.DateiId, cancellationToken);
        return new DokumentDetails((await ZeilenAsync([dokument], cancellationToken))[0], Daten(dokument), datei.Name, dokument.ErstelltAm, dokument.ErstelltVon);
    }

    public async Task<Ergebnis> AnlegenAsync(Guid dateiId, DokumentDaten daten, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(daten);
        if (!await db.Set<Datei>().AnyAsync(f => f.Id == dateiId, cancellationToken))
        {
            return Ergebnis.Fehlgeschlagen("Die Datei wurde nicht gefunden.");
        }

        return await Befehl.AusfuehrenAsync(async () =>
        {
            var dokument = new Dokument(dateiId, daten.Titel.Trim(), daten.Kategorie);
            await UebernehmenAsync(dokument, daten, cancellationToken);
            db.Add(dokument);
            await db.SaveChangesAsync(cancellationToken);
            return dokument.Id;
        });
    }

    public Task<Ergebnis> AendernAsync(Guid id, DokumentDaten daten, CancellationToken cancellationToken = default) =>
        Befehl.AusfuehrenAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(daten);
            var dokument = await db.Set<Dokument>().SingleOrDefaultAsync(d => d.Id == id, cancellationToken) ?? throw new NichtGefundenException();
            await UebernehmenAsync(dokument, daten, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return id;
        });

    public Task<Ergebnis> LoeschenAsync(Guid id, CancellationToken cancellationToken = default) =>
        Befehl.AusfuehrenAsync(async () =>
        {
            var dokument = await db.Set<Dokument>().SingleOrDefaultAsync(d => d.Id == id, cancellationToken) ?? throw new NichtGefundenException();
            db.Remove(dokument);
            await db.SaveChangesAsync(cancellationToken);
            return id;
        });

    public async Task<IReadOnlyList<DokumentZeile>> FreigegebeneAsync(ClaimsPrincipal benutzer, Guid? objektId = null, CancellationToken cancellationToken = default)
    {
        var sichtbar = await DokumentFreigaben.SichtbareAsync(db, benutzer, Zeitzone.Heute(uhr.GetUtcNow()), cancellationToken);
        var dokumente = await sichtbar.OrderByDescending(d => d.Datum ?? DateOnly.MinValue).ThenByDescending(d => d.ErstelltAm).ToListAsync(cancellationToken);
        var zeilen = await ZeilenAsync(dokumente, cancellationToken);
        if (objektId is null)
        {
            return zeilen;
        }

        var zumObjekt = (await SucheAsync(new DokumentSuche(ObjektId: objektId), cancellationToken)).Select(z => z.Id).ToHashSet();
        return zeilen.Where(z => zumObjekt.Contains(z.Id)).ToList();
    }

    public async Task<BezugAuswahl> BezugAuswahlAsync(CancellationToken cancellationToken = default)
    {
        var objekte = await db.Set<Objekt>().AsNoTracking().OrderBy(o => o.Bezeichnung).Select(o => new BezugOption(o.Id, o.Bezeichnung)).ToListAsync(cancellationToken);
        var mietobjekte = await Mietobjekt.LadenAsync(db, cancellationToken);
        var einheiten = mietobjekte.Values.Where(m => m.ZimmerId is null).Select(m => new BezugOption(m.Id, $"{m.Bezeichnung} ({m.Objekt})")).ToList();
        var vertraege = await db.Set<Mietvertrag>().AsNoTracking().Include(v => v.Parteien).ToListAsync(cancellationToken);
        var personIds = vertraege.SelectMany(v => v.Parteien.Select(p => p.PersonId)).Distinct().ToList();
        var personen = await db.Set<Person>().AsNoTracking().Where(p => p.AnonymisiertAm == null).OrderBy(p => p.Nachname).ThenBy(p => p.Vorname).ToListAsync(cancellationToken);
        var namen = personen.ToDictionary(p => p.Id, p => p.Anzeigename);
        return new BezugAuswahl(
            objekte,
            einheiten,
            vertraege.OrderByDescending(v => v.Beginn).Select(v => new BezugOption(v.Id,
                $"{mietobjekte.GetValueOrDefault(v.MietobjektId)?.Bezeichnung}: {VertragsVerwaltung.MieterText(v, namen)} (ab {v.Beginn:dd.MM.yyyy})")).ToList(),
            personen.Select(p => new BezugOption(p.Id, p.Anzeigename)).ToList());
    }

    private async Task UebernehmenAsync(Dokument dokument, DokumentDaten daten, CancellationToken cancellationToken)
    {
        dokument.Titel = daten.Titel.Trim();
        dokument.Kategorie = daten.Kategorie;
        dokument.Datum = daten.Datum;
        dokument.AblaufAm = daten.AblaufAm;
        dokument.Notiz = string.IsNullOrWhiteSpace(daten.Notiz) ? null : daten.Notiz.Trim();
        dokument.FuerMieterFreigegeben = daten.FuerMieterFreigegeben;
        dokument.FuerHausmeisterFreigegeben = daten.FuerHausmeisterFreigegeben;
        dokument.TagsSetzen((daten.Tags ?? "").Split(',', ';'));
        dokument.BezugSetzen(daten.ObjektId, daten.EinheitId, daten.MietvertragId, daten.PersonId);
        dokument.Pruefen();

        if ((daten.ObjektId is { } o && !await db.Set<Objekt>().AnyAsync(x => x.Id == o, cancellationToken))
            || (daten.EinheitId is { } e && !await db.Set<Einheit>().AnyAsync(x => x.Id == e, cancellationToken))
            || (daten.MietvertragId is { } v && !await db.Set<Mietvertrag>().AnyAsync(x => x.Id == v, cancellationToken))
            || (daten.PersonId is { } p && !await db.Set<Person>().AnyAsync(x => x.Id == p, cancellationToken)))
        {
            throw new RegelverletzungException("Ein gewählter Bezug wurde nicht gefunden.");
        }
    }

    private async Task<List<DokumentZeile>> ZeilenAsync(List<Dokument> dokumente, CancellationToken cancellationToken)
    {
        if (dokumente.Count == 0)
        {
            return [];
        }

        var dateiIds = dokumente.Select(d => d.DateiId).ToList();
        var dateien = await db.Set<Datei>().AsNoTracking().Where(f => dateiIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, cancellationToken);
        var objekte = await db.Set<Objekt>().AsNoTracking().ToDictionaryAsync(o => o.Id, o => o.Bezeichnung, cancellationToken);
        var mietobjekte = await Mietobjekt.LadenAsync(db, cancellationToken);
        var vertragIds = dokumente.Where(d => d.MietvertragId is not null).Select(d => d.MietvertragId!.Value).Distinct().ToList();
        var vertraege = await db.Set<Mietvertrag>().AsNoTracking().Where(v => vertragIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, v => v.MietobjektId, cancellationToken);
        var personIds = dokumente.Where(d => d.PersonId is not null).Select(d => d.PersonId!.Value).Distinct().ToList();
        var personen = (await db.Set<Person>().AsNoTracking().Where(p => personIds.Contains(p.Id)).ToListAsync(cancellationToken)).ToDictionary(p => p.Id, p => p.Anzeigename);

        return dokumente.Select(d =>
        {
            var bezug = new List<string>();
            if (d.ObjektId is { } o)
            {
                bezug.Add(objekte.GetValueOrDefault(o, "Objekt"));
            }

            if (d.EinheitId is { } e)
            {
                bezug.Add(mietobjekte.GetValueOrDefault(e)?.Bezeichnung ?? "Einheit");
            }

            if (d.MietvertragId is { } v)
            {
                bezug.Add("Vertrag " + (vertraege.TryGetValue(v, out var m) ? mietobjekte.GetValueOrDefault(m)?.Bezeichnung : ""));
            }

            if (d.PersonId is { } p)
            {
                bezug.Add(personen.GetValueOrDefault(p, "Person"));
            }

            var datei = dateien[d.DateiId];
            return new DokumentZeile(d.Id, d.DateiId, d.Titel, d.Kategorie, d.Datum, d.AblaufAm, d.Tags, string.Join(" · ", bezug), datei.MimeTyp, datei.Groesse, d.FuerMieterFreigegeben, d.FuerHausmeisterFreigegeben);
        }).ToList();
    }

    private static DokumentDaten Daten(Dokument d) => new()
    {
        Titel = d.Titel,
        Kategorie = d.Kategorie,
        Datum = d.Datum,
        AblaufAm = d.AblaufAm,
        Tags = string.Join(", ", d.Tags),
        Notiz = d.Notiz,
        ObjektId = d.ObjektId,
        EinheitId = d.EinheitId,
        MietvertragId = d.MietvertragId,
        PersonId = d.PersonId,
        FuerMieterFreigegeben = d.FuerMieterFreigegeben,
        FuerHausmeisterFreigegeben = d.FuerHausmeisterFreigegeben,
    };
}

/// <summary>
/// Welche freigegebenen Dokumente ein Mieter bzw. Hausmeister sehen darf. Mieter: Dokumente ihrer Verträge und ihrer
/// Person sowie der Einheiten/Objekte, in denen sie aktuell wohnen. Hausmeister: Dokumente ihrer zugeordneten Objekte.
/// </summary>
internal static class DokumentFreigaben
{
    public static async Task<IQueryable<Dokument>> SichtbareAsync(MietHahnDbContext db, ClaimsPrincipal benutzer, DateOnly heute, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(benutzer);
        var keine = db.Set<Dokument>().Where(_ => false);
        if (!Guid.TryParse(benutzer.FindFirstValue(ClaimTypes.NameIdentifier), out var benutzerId))
        {
            return keine;
        }

        var dokumente = db.Set<Dokument>().AsNoTracking();
        if (benutzer.IsInRole(Rollen.Mieter))
        {
            var personIds = await db.Set<Person>().Where(p => p.BenutzerId == benutzerId).Select(p => p.Id).ToListAsync(cancellationToken);
            if (personIds.Count == 0)
            {
                return keine;
            }

            var vertraege = db.Set<Mietvertrag>().Where(v => v.Parteien.Any(p => personIds.Contains(p.PersonId) && p.Rolle == Parteirolle.Mieter));
            var vertragIds = vertraege.Select(v => v.Id);
            var aktuelle = vertraege.Where(v => v.Laufzeit.Von <= heute && (v.Laufzeit.Bis == null || v.Laufzeit.Bis >= heute));
            var einheitIds = aktuelle.Select(v => v.EinheitId);
            var objektIds = db.Set<Einheit>().Where(e => einheitIds.Contains(e.Id)).Select(e => e.ObjektId);
            return dokumente.Where(d => d.FuerMieterFreigegeben && (
                (d.PersonId != null && personIds.Contains(d.PersonId.Value))
                || (d.MietvertragId != null && vertragIds.Contains(d.MietvertragId.Value))
                || (d.EinheitId != null && einheitIds.Contains(d.EinheitId.Value))
                || (d.ObjektId != null && objektIds.Contains(d.ObjektId.Value))));
        }

        if (benutzer.IsInRole(Rollen.Hausmeister))
        {
            var objektIds = db.Set<ObjektZuordnung>().Where(z => z.BenutzerId == benutzerId).Select(z => z.ObjektId);
            var einheitIds = db.Set<Einheit>().Where(e => objektIds.Contains(e.ObjektId)).Select(e => e.Id);
            var vertragIds = db.Set<Mietvertrag>().Where(v => einheitIds.Contains(v.EinheitId)).Select(v => v.Id);
            return dokumente.Where(d => d.FuerHausmeisterFreigegeben && (
                (d.ObjektId != null && objektIds.Contains(d.ObjektId.Value))
                || (d.EinheitId != null && einheitIds.Contains(d.EinheitId.Value))
                || (d.MietvertragId != null && vertragIds.Contains(d.MietvertragId.Value))));
        }

        return keine;
    }
}

/// <summary>Gibt Dateien freigegebener Dokumente für Mieter und Hausmeister zum Download frei (<c>/dateien/{id}</c>).</summary>
internal sealed class DokumentDateiFreigabe(MietHahnDbContext db, TimeProvider uhr) : IDateiFreigabe
{
    public async Task<bool> IstFreigegebenAsync(Guid dateiId, ClaimsPrincipal benutzer, CancellationToken cancellationToken = default) =>
        await (await DokumentFreigaben.SichtbareAsync(db, benutzer, Zeitzone.Heute(uhr.GetUtcNow()), cancellationToken)).AnyAsync(d => d.DateiId == dateiId, cancellationToken);
}

/// <summary>Dashboard: Dokumente, deren Gültigkeit in 60 Tagen endet (z. B. Energieausweis, Versicherung).</summary>
internal sealed class DokumentAblaufAufgaben(MietHahnDbContext db, TimeProvider uhr) : IAufgabenQuelle
{
    public async Task<IReadOnlyList<Aufgabe>> AufgabenAsync(ClaimsPrincipal benutzer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(benutzer);
        if (!benutzer.IsInRole(Rollen.Admin) && !benutzer.HasClaim(Rechte.ClaimTyp, nameof(Recht.Stammdaten)))
        {
            return [];
        }

        var heute = Zeitzone.Heute(uhr.GetUtcNow());
        var grenze = heute.AddDays(60);
        var dokumente = await db.Set<Dokument>().AsNoTracking().Where(d => d.AblaufAm != null && d.AblaufAm <= grenze).OrderBy(d => d.AblaufAm).ToListAsync(cancellationToken);
        return dokumente.Select(d => new Aufgabe(
            "Dokumente",
            d.AblaufAm < heute ? $"Abgelaufen: {d.Titel}" : $"Läuft ab: {d.Titel}",
            $"/verwaltung/dokumente/{d.Id}",
            d.AblaufAm < heute ? Dringlichkeit.Dringend : Dringlichkeit.Bald,
            "Verlängern bzw. neues Dokument ablegen; das Ablaufdatum am alten Dokument dann entfernen.",
            d.AblaufAm)).ToList();
    }
}

/// <summary>Dokumente der Person (direkt oder über ihre Verträge) für die DSGVO-Auskunft.</summary>
internal sealed class DokumentPersonendaten(IDokumentablage ablage) : IPersonendatenQuelle
{
    public string Abschnitt => "dokumente";

    public async Task<object?> DatenAsync(Guid personId, CancellationToken cancellationToken = default)
    {
        var dokumente = await ablage.SucheAsync(new DokumentSuche(PersonId: personId), cancellationToken);
        return dokumente.Count == 0 ? null : dokumente.Select(d => new { d.Titel, Kategorie = d.Kategorie.ToString(), d.Datum, d.Bezug, Datei = $"/dateien/{d.DateiId}" }).ToList();
    }
}
