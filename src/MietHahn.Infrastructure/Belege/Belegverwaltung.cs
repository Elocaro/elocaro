using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MietHahn.Application.Aufgaben;
using MietHahn.Application.Belege;
using MietHahn.Application.Gemeinsam;
using MietHahn.Application.Konten;
using MietHahn.Application.Personen;
using MietHahn.Domain.Belege;
using MietHahn.Domain.Dateien;
using MietHahn.Domain.Gemeinsam;
using MietHahn.Domain.Objekte;
using MietHahn.Domain.Personen;
using MietHahn.Infrastructure.Gemeinsam;
using MietHahn.Infrastructure.Persistenz;

namespace MietHahn.Infrastructure.Belege;

internal sealed class Belegverwaltung(MietHahnDbContext db) : IBelegverwaltung
{
    public async Task<IReadOnlyList<BelegZeile>> SucheAsync(BelegSuche suche, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(suche);
        var abfrage = db.Set<Beleg>().AsNoTracking().Include(b => b.Positionen).Include(b => b.Dateien).AsSplitQuery().AsQueryable();
        if (suche.Jahr is { } jahr)
        {
            var von = new DateOnly(jahr, 1, 1);
            var bis = new DateOnly(jahr, 12, 31);
            abfrage = abfrage.Where(b => (b.Rechnungsdatum >= von && b.Rechnungsdatum <= bis)
                || (b.LeistungVon != null && b.LeistungVon <= bis && (b.LeistungBis == null || b.LeistungBis >= von)));
        }

        if (suche.ObjektId is { } objekt)
        {
            abfrage = abfrage.Where(b => b.Positionen.Any(p => p.ObjektId == objekt));
        }

        if (!string.IsNullOrWhiteSpace(suche.Kostenart))
        {
            abfrage = abfrage.Where(b => b.Positionen.Any(p => p.Kostenart == suche.Kostenart));
        }

        if (suche.OhneZahlung)
        {
            abfrage = abfrage.Where(b => b.BezahltAm == null);
        }

        if (!string.IsNullOrWhiteSpace(suche.Text))
        {
            var muster = "%" + suche.Text.Trim().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
            var lieferanten = db.Set<Person>().Where(p => EF.Functions.ILike(p.Nachname, muster) || (p.Firma != null && EF.Functions.ILike(p.Firma, muster))).Select(p => p.Id);
            abfrage = abfrage.Where(b => (b.LieferantName != null && EF.Functions.ILike(b.LieferantName, muster))
                || (b.Rechnungsnummer != null && EF.Functions.ILike(b.Rechnungsnummer, muster))
                || (b.Notiz != null && EF.Functions.ILike(b.Notiz, muster))
                || (b.LieferantId != null && lieferanten.Contains(b.LieferantId.Value)));
        }

        var belege = await abfrage.OrderByDescending(b => b.Rechnungsdatum).ThenByDescending(b => b.Id).ToListAsync(cancellationToken);
        if (suche.NichtAufgeteilt)
        {
            belege = belege.Where(b => !b.IstVollstaendigAufgeteilt).ToList();
        }

        var namen = await LieferantenAsync(belege.Where(b => b.LieferantId is not null).Select(b => b.LieferantId!.Value), cancellationToken);
        var objekte = await db.Set<Objekt>().AsNoTracking().ToDictionaryAsync(o => o.Id, o => o.Bezeichnung, cancellationToken);
        return belege.Select(b => new BelegZeile(
            b.Id,
            Lieferant(b, namen),
            b.Rechnungsnummer,
            b.Rechnungsdatum,
            b.Brutto,
            b.Aufgeteilt,
            b.Positionen.Select(p => Kostenarten.Finden(p.Kostenart)?.Bezeichnung ?? p.Kostenart).Distinct().ToList(),
            b.Positionen.Select(p => objekte.GetValueOrDefault(p.ObjektId, "?")).Distinct().ToList(),
            b.BezahltAm,
            b.Dateien.Count)).ToList();
    }

    public async Task<BelegDetails?> LadenAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var beleg = await db.Set<Beleg>().AsNoTracking().Include(b => b.Positionen).Include(b => b.Dateien).AsSplitQuery().SingleOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (beleg is null)
        {
            return null;
        }

        var dateiIds = beleg.Dateien.Select(d => d.DateiId).ToList();
        var dateien = await db.Set<Datei>().AsNoTracking().Where(d => dateiIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, cancellationToken);
        var objekte = await db.Set<Objekt>().AsNoTracking().ToDictionaryAsync(o => o.Id, o => o.Bezeichnung, cancellationToken);
        var einheiten = await db.Set<Einheit>().AsNoTracking().ToDictionaryAsync(e => e.Id, e => e.Bezeichnung, cancellationToken);
        var namen = await LieferantenAsync(beleg.LieferantId is { } l ? [l] : [], cancellationToken);
        return new BelegDetails(
            beleg.Id,
            Daten(beleg),
            Lieferant(beleg, namen),
            beleg.Dateien.OrderBy(d => d.Seite).Select(d => new BelegDateiAnsicht(d.DateiId, dateien[d.DateiId].Name, dateien[d.DateiId].MimeTyp, d.Seite)).ToList(),
            beleg.Positionen.OrderBy(p => p.ErstelltAm).ThenBy(p => p.Id).Select(p => new PositionAnsicht(
                p.Id, p.Kostenart, Kostenarten.Finden(p.Kostenart)?.Bezeichnung ?? p.Kostenart, p.Betrag,
                objekte.GetValueOrDefault(p.ObjektId, "?"), p.EinheitId is { } e ? einheiten.GetValueOrDefault(e) : null,
                p.Abrechnungszeitraum, p.Umlagefaehig, p.Paragraf35a, p.Arbeitskosten, p.Notiz)).ToList(),
            beleg.Aufgeteilt,
            beleg.Brutto - beleg.Aufgeteilt);
    }

    public Task<Ergebnis> AnlegenAsync(BelegDaten daten, IReadOnlyList<Guid> dateiIds, PositionEingabe? position = null, CancellationToken cancellationToken = default) =>
        Befehl.AusfuehrenAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(daten);
            ArgumentNullException.ThrowIfNull(dateiIds);
            var beleg = new Beleg(daten.Rechnungsdatum ?? throw new RegelverletzungException("Bitte das Rechnungsdatum angeben."), daten.Brutto ?? 0m);
            await UebernehmenAsync(beleg, daten, cancellationToken);
            await DateienPruefenAsync(dateiIds, cancellationToken);
            foreach (var dateiId in dateiIds)
            {
                beleg.DateiHinzufuegen(dateiId);
            }

            if (position is not null && !string.IsNullOrWhiteSpace(position.Kostenart))
            {
                await PositionAnlegenAsync(beleg, position, cancellationToken);
            }

            db.Add(beleg);
            await db.SaveChangesAsync(cancellationToken);
            return beleg.Id;
        });

    public Task<Ergebnis> AendernAsync(Guid id, BelegDaten daten, CancellationToken cancellationToken = default) =>
        Befehl.AusfuehrenAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(daten);
            var beleg = await BelegAsync(id, cancellationToken);
            await UebernehmenAsync(beleg, daten, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return id;
        });

    public Task<Ergebnis> DateienHinzufuegenAsync(Guid id, IReadOnlyList<Guid> dateiIds, CancellationToken cancellationToken = default) =>
        Befehl.AusfuehrenAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(dateiIds);
            var beleg = await BelegAsync(id, cancellationToken);
            await DateienPruefenAsync(dateiIds, cancellationToken);
            foreach (var dateiId in dateiIds)
            {
                beleg.DateiHinzufuegen(dateiId);
            }

            await db.SaveChangesAsync(cancellationToken);
            return id;
        });

    public Task<Ergebnis> DateiEntfernenAsync(Guid id, Guid dateiId, CancellationToken cancellationToken = default) =>
        Befehl.AusfuehrenAsync(async () =>
        {
            var beleg = await BelegAsync(id, cancellationToken);
            beleg.DateiEntfernen(dateiId);
            await db.SaveChangesAsync(cancellationToken);
            return id;
        });

    public Task<Ergebnis> PositionHinzufuegenAsync(Guid id, PositionEingabe eingabe, CancellationToken cancellationToken = default) =>
        Befehl.AusfuehrenAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(eingabe);
            var beleg = await BelegAsync(id, cancellationToken);
            var position = await PositionAnlegenAsync(beleg, eingabe, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return position.Id;
        });

    public Task<Ergebnis> PositionEntfernenAsync(Guid id, Guid positionId, CancellationToken cancellationToken = default) =>
        Befehl.AusfuehrenAsync(async () =>
        {
            var beleg = await BelegAsync(id, cancellationToken);
            beleg.PositionEntfernen(positionId);
            await db.SaveChangesAsync(cancellationToken);
            return id;
        });

    public Task<Ergebnis> LoeschenAsync(Guid id, CancellationToken cancellationToken = default) =>
        Befehl.AusfuehrenAsync(async () =>
        {
            var beleg = await BelegAsync(id, cancellationToken);
            db.Remove(beleg);
            await db.SaveChangesAsync(cancellationToken);
            return id;
        });

    public async Task<BelegAuswahl> AuswahlAsync(CancellationToken cancellationToken = default)
    {
        var lieferanten = await db.Set<Person>().AsNoTracking().Where(p => p.Kontaktart != Kontaktart.Keine && p.AnonymisiertAm == null).ToListAsync(cancellationToken);
        var objekte = await db.Set<Objekt>().AsNoTracking().OrderBy(o => o.Bezeichnung).Select(o => new BezugOption(o.Id, o.Bezeichnung)).ToListAsync(cancellationToken);
        var einheiten = await db.Set<Einheit>().AsNoTracking().Join(db.Set<Objekt>(), e => e.ObjektId, o => o.Id, (e, o) => new { e.Id, e.Bezeichnung, Objekt = o.Bezeichnung })
            .OrderBy(e => e.Objekt).ThenBy(e => e.Bezeichnung).ToListAsync(cancellationToken);
        return new BelegAuswahl(
            lieferanten.OrderBy(p => p.Anzeigename, StringComparer.CurrentCulture).Select(p => new BezugOption(p.Id, p.Anzeigename)).ToList(),
            objekte,
            einheiten.Select(e => new BezugOption(e.Id, $"{e.Bezeichnung} ({e.Objekt})")).ToList());
    }

    private async Task<Kostenposition> PositionAnlegenAsync(Beleg beleg, PositionEingabe eingabe, CancellationToken cancellationToken)
    {
        var objektId = eingabe.ObjektId ?? throw new RegelverletzungException("Bitte das Objekt wählen.");
        if (!await db.Set<Objekt>().AnyAsync(o => o.Id == objektId, cancellationToken))
        {
            throw new RegelverletzungException("Das Objekt wurde nicht gefunden.");
        }

        if (eingabe.EinheitId is { } einheit && !await db.Set<Einheit>().AnyAsync(e => e.Id == einheit && e.ObjektId == objektId, cancellationToken))
        {
            throw new RegelverletzungException("Die Einheit gehört nicht zum gewählten Objekt.");
        }

        // Standard: Leistungszeitraum, sonst Kalenderjahr der Rechnung.
        var von = eingabe.Von ?? beleg.LeistungVon ?? new DateOnly(beleg.Rechnungsdatum.Year, 1, 1);
        var bis = eingabe.Bis ?? (eingabe.Von is null ? beleg.LeistungBis : null) ?? new DateOnly(von.Year, 12, 31);
        if (bis < von)
        {
            throw new RegelverletzungException("Der Abrechnungszeitraum endet vor seinem Beginn.");
        }

        var position = beleg.PositionHinzufuegen(eingabe.Kostenart, eingabe.Betrag ?? beleg.Brutto - beleg.Aufgeteilt, objektId, new Zeitraum(von, bis), eingabe.Umlagefaehig);
        position.EinheitId = eingabe.EinheitId;
        position.Notiz = Leer(eingabe.Notiz);
        position.Paragraf35aSetzen(eingabe.Paragraf35a, eingabe.Arbeitskosten);
        return position;
    }

    private async Task UebernehmenAsync(Beleg beleg, BelegDaten daten, CancellationToken cancellationToken)
    {
        if (daten.LieferantId is { } lieferant && !await db.Set<Person>().AnyAsync(p => p.Id == lieferant, cancellationToken))
        {
            throw new RegelverletzungException("Der Lieferant wurde nicht gefunden.");
        }

        beleg.LieferantId = daten.LieferantId;
        beleg.LieferantName = Leer(daten.LieferantName);
        beleg.Rechnungsnummer = Leer(daten.Rechnungsnummer);
        beleg.Rechnungsdatum = daten.Rechnungsdatum ?? throw new RegelverletzungException("Bitte das Rechnungsdatum angeben.");
        beleg.LeistungszeitraumSetzen(daten.LeistungVon, daten.LeistungBis);
        beleg.Netto = daten.Netto;
        beleg.Umsatzsteuer = daten.Umsatzsteuer;
        beleg.BruttoSetzen(daten.Brutto ?? 0m);
        beleg.FaelligAm = daten.FaelligAm;
        beleg.BezahltAm = daten.BezahltAm;
        beleg.Brennstoffmenge = daten.Brennstoffmenge;
        beleg.Brennstoffeinheit = Leer(daten.Brennstoffeinheit);
        beleg.Co2Kg = daten.Co2Kg;
        beleg.Notiz = Leer(daten.Notiz);
        beleg.Pruefen();
    }

    private async Task DateienPruefenAsync(IReadOnlyList<Guid> dateiIds, CancellationToken cancellationToken)
    {
        var ids = dateiIds.Distinct().ToList();
        if (await db.Set<Datei>().CountAsync(d => ids.Contains(d.Id), cancellationToken) != ids.Count)
        {
            throw new RegelverletzungException("Eine Datei wurde nicht gefunden.");
        }
    }

    private async Task<Beleg> BelegAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Set<Beleg>().Include(b => b.Positionen).Include(b => b.Dateien).AsSplitQuery().SingleOrDefaultAsync(b => b.Id == id, cancellationToken)
            ?? throw new NichtGefundenException();

    private async Task<Dictionary<Guid, string>> LieferantenAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        var liste = ids.Distinct().ToList();
        return (await db.Set<Person>().AsNoTracking().Where(p => liste.Contains(p.Id)).ToListAsync(cancellationToken)).ToDictionary(p => p.Id, p => p.Anzeigename);
    }

    private static string Lieferant(Beleg b, Dictionary<Guid, string> namen) =>
        b.LieferantId is { } id && namen.TryGetValue(id, out var name) ? name : b.LieferantName ?? "?";

    private static BelegDaten Daten(Beleg b) => new()
    {
        LieferantId = b.LieferantId,
        LieferantName = b.LieferantName,
        Rechnungsnummer = b.Rechnungsnummer,
        Rechnungsdatum = b.Rechnungsdatum,
        LeistungVon = b.LeistungVon,
        LeistungBis = b.LeistungBis,
        Brutto = b.Brutto,
        Netto = b.Netto,
        Umsatzsteuer = b.Umsatzsteuer,
        FaelligAm = b.FaelligAm,
        BezahltAm = b.BezahltAm,
        Brennstoffmenge = b.Brennstoffmenge,
        Brennstoffeinheit = b.Brennstoffeinheit,
        Co2Kg = b.Co2Kg,
        Notiz = b.Notiz,
    };

    private static string? Leer(string? wert) => string.IsNullOrWhiteSpace(wert) ? null : wert.Trim();
}

/// <summary>Dashboard: fällige, unbezahlte Belege und nicht aufgeteilte Belege.</summary>
internal sealed class BelegAufgaben(MietHahnDbContext db, TimeProvider uhr) : IAufgabenQuelle
{
    public async Task<IReadOnlyList<Aufgabe>> AufgabenAsync(ClaimsPrincipal benutzer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(benutzer);
        if (!benutzer.IsInRole(Rollen.Admin) && !benutzer.HasClaim(Rechte.ClaimTyp, nameof(Recht.Finanzen)))
        {
            return [];
        }

        var heute = Zeitzone.Heute(uhr.GetUtcNow());
        var aufgaben = new List<Aufgabe>();
        var faellig = await db.Set<Beleg>().AsNoTracking().Where(b => b.BezahltAm == null && b.FaelligAm != null && b.FaelligAm <= heute.AddDays(7)).OrderBy(b => b.FaelligAm).ToListAsync(cancellationToken);
        aufgaben.AddRange(faellig.Select(b => new Aufgabe("Belege", $"Rechnung zahlen: {b.LieferantName ?? "Beleg"} {b.Brutto:0.00} €", $"/verwaltung/belege/{b.Id}",
            b.FaelligAm < heute ? Dringlichkeit.Dringend : Dringlichkeit.Bald, b.Rechnungsnummer is null ? null : $"Rechnung {b.Rechnungsnummer}", b.FaelligAm)));

        var belege = await db.Set<Beleg>().AsNoTracking().Include(b => b.Positionen).ToListAsync(cancellationToken);
        var offen = belege.Count(b => !b.IstVollstaendigAufgeteilt);
        if (offen > 0)
        {
            aufgaben.Add(new Aufgabe("Belege", $"{offen} Belege ohne vollständige Kostenaufteilung", "/verwaltung/belege?aufteilen=true", Dringlichkeit.Info,
                "Ohne Kostenart und Objekt fehlen sie in Nebenkostenabrechnung und Anlage V."));
        }

        return aufgaben;
    }
}

/// <summary>Belege eines Lieferanten (Kontakt) für die DSGVO-Auskunft.</summary>
internal sealed class BelegPersonendaten(MietHahnDbContext db) : IPersonendatenQuelle
{
    public string Abschnitt => "belege";

    public async Task<object?> DatenAsync(Guid personId, CancellationToken cancellationToken = default)
    {
        var belege = await db.Set<Beleg>().AsNoTracking().Where(b => b.LieferantId == personId).OrderBy(b => b.Rechnungsdatum)
            .Select(b => new { b.Rechnungsnummer, b.Rechnungsdatum, b.Brutto }).ToListAsync(cancellationToken);
        return belege.Count == 0 ? null : belege;
    }
}
