using System.Globalization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MietHahn.Application.Aufgaben;
using MietHahn.Application.Finanzen;
using MietHahn.Application.Gemeinsam;
using MietHahn.Application.Konten;
using MietHahn.Application.Personen;
using MietHahn.Domain.Finanzen;
using MietHahn.Domain.Gemeinsam;
using MietHahn.Domain.Personen;
using MietHahn.Domain.Vertraege;
using MietHahn.Infrastructure.Gemeinsam;
using MietHahn.Infrastructure.Persistenz;
using MietHahn.Infrastructure.Vertraege;
using Quartz;

namespace MietHahn.Infrastructure.Finanzen;

internal sealed class Mieterkonten(MietHahnDbContext db, TimeProvider uhr) : IMieterkonten
{
    public async Task<Mieterkonto?> LadenAsync(Guid mietvertragId, CancellationToken cancellationToken = default)
    {
        var vertrag = await db.Set<Mietvertrag>().AsNoTracking().Include(v => v.Parteien).SingleOrDefaultAsync(v => v.Id == mietvertragId, cancellationToken);
        if (vertrag is null)
        {
            return null;
        }

        var einstellungen = await Finanzeinstellungsverwaltung.LadenOderNeuAsync(db, cancellationToken);
        var (mietobjekt, mieter) = (await BeschreibungenAsync([vertrag], cancellationToken))[vertrag.Id];
        var sollstellungen = await db.Set<Sollstellung>().AsNoTracking().Where(s => s.MietvertragId == mietvertragId).ToListAsync(cancellationToken);
        var zahlungen = await db.Set<Zahlung>().AsNoTracking().Where(z => z.MietvertragId == mietvertragId).ToListAsync(cancellationToken);
        var ergebnis = Verrechnen(sollstellungen, zahlungen, einstellungen.Verrechnung);
        var heute = Zeitzone.Heute(uhr.GetUtcNow());

        var zeilen = sollstellungen.Select(s => (Datum: s.Faellig, Reihenfolge: Rangfolge(s), s.Id, Zeile: (Func<decimal, Kontobuchung>)(saldo => new Kontobuchung(
                s.Id, Buchungsart.Sollstellung, s.Faellig, s.Bezeichnung, s.Art, Math.Max(s.Betrag, 0), Math.Max(-s.Betrag, 0), saldo,
                s.Betrag > 0 ? ergebnis.Offen[s.Id] : null, s.Automatisch, null)), Betrag: s.Betrag))
            .Concat(zahlungen.Select(z => (Datum: z.Datum, Reihenfolge: 1000, z.Id, Zeile: (Func<decimal, Kontobuchung>)(saldo => new Kontobuchung(
                z.Id, Buchungsart.Zahlung, z.Datum, Zahlungstext(z), null, Math.Max(-z.Betrag, 0), Math.Max(z.Betrag, 0), saldo,
                null, false, z.Herkunft)), Betrag: -z.Betrag)))
            .OrderBy(z => z.Datum).ThenBy(z => z.Reihenfolge).ThenBy(z => z.Id);

        var buchungen = new List<Kontobuchung>();
        var laufend = 0m;
        foreach (var zeile in zeilen)
        {
            laufend += zeile.Betrag;
            buchungen.Add(zeile.Zeile(laufend));
        }

        var faellig = sollstellungen.Where(s => s.Betrag > 0 && s.Faellig <= heute).Sum(s => ergebnis.Offen[s.Id]);
        return new Mieterkonto(vertrag.Id, mietobjekt, mieter, einstellungen.BuchhaltungAb, buchungen, laufend, faellig,
            Jahreswerte(sollstellungen, ergebnis), einstellungen.Verrechnung);
    }

    public async Task<int> SollstellungenErzeugenAsync(Guid? mietvertragId = null, CancellationToken cancellationToken = default)
    {
        var einstellungen = await Finanzeinstellungsverwaltung.LadenOderNeuAsync(db, cancellationToken);
        if (einstellungen.BuchhaltungAb is not { } ab)
        {
            return 0;
        }

        var heute = Zeitzone.Heute(uhr.GetUtcNow());
        var bisMonat = new DateOnly(heute.Year, heute.Month, 1);
        var abfrage = db.Set<Mietvertrag>().AsNoTracking().Include(v => v.Bestandteile).AsQueryable();
        if (mietvertragId is { } id)
        {
            abfrage = abfrage.Where(v => v.Id == id);
        }

        var vertraege = await abfrage.ToListAsync(cancellationToken);
        var vertragIds = vertraege.Select(v => v.Id).ToList();
        var vorhanden = (await db.Set<Sollstellung>().Where(s => s.Art == Postenart.Miete && vertragIds.Contains(s.MietvertragId)).ToListAsync(cancellationToken))
            .ToLookup(s => s.MietvertragId);

        var aenderungen = 0;
        foreach (var vertrag in vertraege)
        {
            var bestehend = vorhanden[vertrag.Id].ToDictionary(s => (s.Leistungsmonat!.Value, s.Bestandteil!.Value));
            var soll = new HashSet<(DateOnly, Bestandteilart)>();
            foreach (var monat in Sollberechnung.Monate(vertrag, ab, bisMonat))
            {
                // Fällig am n-ten Werktag, bei Einzug im Monat frühestens am Einzugstag.
                var faellig = Werktage.NterWerktag(monat.Year, monat.Month, vertrag.FaelligAmWerktag);
                if (vertrag.Beginn > faellig)
                {
                    faellig = vertrag.Beginn;
                }

                foreach (var posten in Sollberechnung.Monat(vertrag, monat.Year, monat.Month, ab))
                {
                    soll.Add((monat, posten.Bestandteil));
                    if (bestehend.TryGetValue((monat, posten.Bestandteil), out var sollstellung))
                    {
                        aenderungen += sollstellung.Angleichen(faellig, posten.Betrag, posten.Bezeichnung) ? 1 : 0;
                    }
                    else
                    {
                        db.Add(Sollstellung.Monatsmiete(vertrag.Id, monat, posten.Bestandteil, faellig, posten.Betrag, posten.Bezeichnung));
                        aenderungen++;
                    }
                }
            }

            // Vertrag verkürzt, Bestandteil entfallen oder Buchhaltungsbeginn verschoben.
            foreach (var ueberzaehlig in bestehend.Where(b => !soll.Contains(b.Key)))
            {
                db.Remove(ueberzaehlig.Value);
                aenderungen++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return aenderungen;
    }

    public Task<Ergebnis> SollstellungAnlegenAsync(Guid mietvertragId, SollstellungEingabe eingabe, CancellationToken cancellationToken = default) =>
        Befehl.AusfuehrenAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(eingabe);
            await VertragPruefenAsync(mietvertragId, cancellationToken);
            if (eingabe.Art == Postenart.Miete)
            {
                throw new RegelverletzungException("Monatsmieten entstehen automatisch aus dem Vertrag.");
            }

            var betrag = eingabe.Betrag ?? 0m;
            var sollstellung = new Sollstellung(
                mietvertragId,
                eingabe.Art,
                eingabe.Faellig ?? throw new RegelverletzungException("Bitte das Fälligkeitsdatum angeben."),
                eingabe.Art == Postenart.Erstattung ? -betrag : betrag,
                string.IsNullOrWhiteSpace(eingabe.Bezeichnung) ? Standardtext(eingabe.Art) : eingabe.Bezeichnung.Trim())
            {
                Notiz = string.IsNullOrWhiteSpace(eingabe.Notiz) ? null : eingabe.Notiz.Trim(),
            };
            db.Add(sollstellung);
            await db.SaveChangesAsync(cancellationToken);
            return sollstellung.Id;
        });

    public Task<Ergebnis> SollstellungLoeschenAsync(Guid sollstellungId, CancellationToken cancellationToken = default) =>
        Befehl.AusfuehrenAsync(async () =>
        {
            var sollstellung = await db.Set<Sollstellung>().SingleOrDefaultAsync(s => s.Id == sollstellungId, cancellationToken) ?? throw new NichtGefundenException();
            if (sollstellung.Automatisch)
            {
                throw new RegelverletzungException("Monatsmieten ergeben sich aus dem Vertrag und lassen sich nicht löschen; bei Bedarf eine Gutschrift (Erstattung) anlegen.");
            }

            // Tilgungsbestimmungen auf diese Forderung entfallen (FK ON DELETE SET NULL greift beim logischen Löschen nicht).
            await db.Set<Zahlung>().Where(z => z.SollstellungId == sollstellungId).ExecuteUpdateAsync(u => u.SetProperty(z => z.SollstellungId, (Guid?)null), cancellationToken);
            db.Remove(sollstellung);
            await db.SaveChangesAsync(cancellationToken);
            return sollstellung.Id;
        });

    public Task<Ergebnis> ZahlungErfassenAsync(Guid mietvertragId, ZahlungEingabe eingabe, CancellationToken cancellationToken = default) =>
        Befehl.AusfuehrenAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(eingabe);
            await VertragPruefenAsync(mietvertragId, cancellationToken);
            if (eingabe.SollstellungId is { } bestimmt && !await db.Set<Sollstellung>().AnyAsync(s => s.Id == bestimmt && s.MietvertragId == mietvertragId, cancellationToken))
            {
                throw new RegelverletzungException("Die gewählte Forderung gehört nicht zu diesem Mietverhältnis.");
            }

            var betrag = eingabe.Betrag ?? 0m;
            var zahlung = new Zahlung(mietvertragId, eingabe.Datum ?? throw new RegelverletzungException("Bitte das Zahlungsdatum angeben."), eingabe.Auszahlung ? -betrag : betrag)
            {
                Verwendungszweck = string.IsNullOrWhiteSpace(eingabe.Verwendungszweck) ? null : eingabe.Verwendungszweck.Trim(),
                SollstellungId = eingabe.Auszahlung ? null : eingabe.SollstellungId,
            };
            db.Add(zahlung);
            await db.SaveChangesAsync(cancellationToken);
            return zahlung.Id;
        });

    public Task<Ergebnis> ZahlungLoeschenAsync(Guid zahlungId, CancellationToken cancellationToken = default) =>
        Befehl.AusfuehrenAsync(async () =>
        {
            var zahlung = await db.Set<Zahlung>().SingleOrDefaultAsync(z => z.Id == zahlungId, cancellationToken) ?? throw new NichtGefundenException();
            if (zahlung.Herkunft != Zahlungsherkunft.Manuell)
            {
                throw new RegelverletzungException("Zahlungen aus dem Bankimport werden über die Zuordnung des Umsatzes geändert.");
            }

            db.Remove(zahlung);
            await db.SaveChangesAsync(cancellationToken);
            return zahlung.Id;
        });

    public async Task<IReadOnlyList<OffenerPosten>> OffenePostenAsync(CancellationToken cancellationToken = default)
    {
        var heute = Zeitzone.Heute(uhr.GetUtcNow());
        var einstellungen = await Finanzeinstellungsverwaltung.LadenOderNeuAsync(db, cancellationToken);
        var sollstellungen = (await db.Set<Sollstellung>().AsNoTracking().ToListAsync(cancellationToken)).ToLookup(s => s.MietvertragId);
        var zahlungen = (await db.Set<Zahlung>().AsNoTracking().ToListAsync(cancellationToken)).ToLookup(z => z.MietvertragId);
        var vertragIds = sollstellungen.Select(g => g.Key).ToList();
        var vertraege = await db.Set<Mietvertrag>().AsNoTracking().Include(v => v.Parteien).Where(v => vertragIds.Contains(v.Id)).ToListAsync(cancellationToken);
        var beschreibungen = await BeschreibungenAsync(vertraege, cancellationToken);

        var posten = new List<OffenerPosten>();
        foreach (var vertrag in vertraege)
        {
            var ergebnis = Verrechnen(sollstellungen[vertrag.Id].ToList(), zahlungen[vertrag.Id].ToList(), einstellungen.Verrechnung);
            var (mietobjekt, mieter) = beschreibungen[vertrag.Id];
            posten.AddRange(sollstellungen[vertrag.Id]
                .Where(s => s.Betrag > 0 && s.Faellig <= heute && ergebnis.Offen[s.Id] > 0)
                .Select(s => new OffenerPosten(s.Id, vertrag.Id, mietobjekt, mieter, s.Art, s.Bezeichnung, s.Faellig, s.Betrag, ergebnis.Offen[s.Id], heute.DayNumber - s.Faellig.DayNumber)));
        }

        return posten.OrderBy(p => p.Faellig).ThenBy(p => p.Mietobjekt, StringComparer.CurrentCulture).ThenBy(p => Rangfolge(sollstellungen[p.MietvertragId].Single(s => s.Id == p.SollstellungId))).ToList();
    }

    /// <summary>Forderungen: positive Sollstellungen und Auszahlungen; Guthaben: Zahlungen und Gutschriften.</summary>
    internal static Verrechnungsergebnis Verrechnen(IReadOnlyCollection<Sollstellung> sollstellungen, IReadOnlyCollection<Zahlung> zahlungen, Verrechnungsreihenfolge reihenfolge)
    {
        var forderungen = sollstellungen.Where(s => s.Betrag > 0).Select(s => new Verrechnungsposten(s.Id, s.Faellig, s.Betrag, s.Rang, s.Automatisch))
            .Concat(zahlungen.Where(z => z.Betrag < 0).Select(z => new Verrechnungsposten(z.Id, z.Datum, -z.Betrag)));
        var guthaben = zahlungen.Where(z => z.Betrag > 0).Select(z => new Verrechnungsposten(z.Id, z.Datum, z.Betrag, Bestimmung: z.SollstellungId))
            .Concat(sollstellungen.Where(s => s.Betrag < 0).Select(s => new Verrechnungsposten(s.Id, s.Faellig, -s.Betrag)));
        return Verrechnung.Verteilen(forderungen, guthaben, reihenfolge);
    }

    private static List<Jahreswert> Jahreswerte(List<Sollstellung> sollstellungen, Verrechnungsergebnis ergebnis)
    {
        var mieten = sollstellungen.Where(s => s.Automatisch).ToDictionary(s => s.Id);
        var soll = mieten.Values.Select(s => (s.Leistungsmonat!.Value.Year, s.Bestandteil!.Value, Soll: s.Betrag, Getilgt: ergebnis.Getilgt(s.Id), Zufluss: 0m));
        var zufluss = ergebnis.Anteile.Where(a => mieten.ContainsKey(a.ForderungId))
            .Select(a => (a.Datum.Year, mieten[a.ForderungId].Bestandteil!.Value, Soll: 0m, Getilgt: 0m, Zufluss: a.Betrag));
        return soll.Concat(zufluss)
            .GroupBy(w => (w.Year, w.Value))
            .Select(g => new Jahreswert(g.Key.Year, g.Key.Value, g.Sum(w => w.Soll), g.Sum(w => w.Getilgt), g.Sum(w => w.Zufluss)))
            .OrderByDescending(w => w.Jahr).ThenBy(w => w.Bestandteil)
            .ToList();
    }

    private async Task<Dictionary<Guid, (string Mietobjekt, string Mieter)>> BeschreibungenAsync(List<Mietvertrag> vertraege, CancellationToken cancellationToken)
    {
        var mietobjekte = await Mietobjekt.LadenAsync(db, cancellationToken);
        var personIds = vertraege.SelectMany(v => v.Parteien.Select(p => p.PersonId)).Distinct().ToList();
        var namen = (await db.Set<Person>().AsNoTracking().Where(p => personIds.Contains(p.Id)).ToListAsync(cancellationToken)).ToDictionary(p => p.Id, p => p.Anzeigename);
        return vertraege.ToDictionary(v => v.Id, v =>
        {
            var m = mietobjekte.GetValueOrDefault(v.MietobjektId);
            return (m is null ? "?" : $"{m.Objekt}, {m.Bezeichnung}", VertragsVerwaltung.MieterText(v, namen));
        });
    }

    private async Task VertragPruefenAsync(Guid mietvertragId, CancellationToken cancellationToken)
    {
        if (!await db.Set<Mietvertrag>().AnyAsync(v => v.Id == mietvertragId, cancellationToken))
        {
            throw new NichtGefundenException();
        }
    }

    /// <summary>Am selben Tag: Bestandteile der Monatsmiete in fester Reihenfolge, dann sonstige Forderungen.</summary>
    private static int Rangfolge(Sollstellung s) => s.Bestandteil is { } b ? (int)b : 100;

    private static string Zahlungstext(Zahlung z) => (z.Betrag < 0 ? "Auszahlung" : "Zahlung") + (z.Verwendungszweck is { } zweck ? $": {zweck}" : "");

    internal static string Standardtext(Postenart art) => art switch
    {
        Postenart.Nachzahlung => "Nachzahlung",
        Postenart.Erstattung => "Erstattung (Guthaben)",
        Postenart.Mahngebuehr => "Mahngebühr",
        Postenart.Ruecklastschriftgebuehr => "Rücklastschriftgebühr",
        Postenart.Verzugszinsen => "Verzugszinsen",
        Postenart.Schadensersatz => "Schadensersatz",
        Postenart.Kaution => "Kaution",
        Postenart.Saldovortrag => "Saldovortrag",
        Postenart.Miete => "Miete",
        _ => "Sonstige Forderung",
    };
}

internal sealed class Finanzeinstellungsverwaltung(MietHahnDbContext db) : IFinanzeinstellungen
{
    public async Task<FinanzeinstellungenDaten> LadenAsync(CancellationToken cancellationToken = default)
    {
        var einstellungen = await LadenOderNeuAsync(db, cancellationToken);
        return new FinanzeinstellungenDaten { BuchhaltungAb = einstellungen.BuchhaltungAb, Verrechnung = einstellungen.Verrechnung };
    }

    public async Task SpeichernAsync(FinanzeinstellungenDaten daten, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(daten);
        var einstellungen = await LadenOderNeuAsync(db, cancellationToken);
        if (db.Entry(einstellungen).State == EntityState.Detached)
        {
            db.Add(einstellungen);
        }

        einstellungen.BuchhaltungAbSetzen(daten.BuchhaltungAb);
        einstellungen.Verrechnung = daten.Verrechnung;
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Der einzige Datensatz oder ein ungespeicherter mit Standardwerten.</summary>
    internal static async Task<Finanzeinstellungen> LadenOderNeuAsync(MietHahnDbContext db, CancellationToken cancellationToken) =>
        await db.Set<Finanzeinstellungen>().OrderBy(f => f.ErstelltAm).FirstOrDefaultAsync(cancellationToken) ?? new Finanzeinstellungen();
}

/// <summary>Erzeugt täglich (idempotent) die Monatsmieten des laufenden Monats und holt fehlende nach.</summary>
[DisallowConcurrentExecution]
internal sealed class SollstellungJob(IMieterkonten konten) : IJob
{
    public static readonly JobKey Schluessel = new("sollstellungen", "finanzen");

    public async Task Execute(IJobExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        await konten.SollstellungenErzeugenAsync(null, context.CancellationToken);
    }
}

/// <summary>Dashboard: Buchhaltungsbeginn fehlt; fällige Rückstände.</summary>
internal sealed class FinanzAufgaben(MietHahnDbContext db, IMieterkonten konten) : IAufgabenQuelle
{
    public async Task<IReadOnlyList<Aufgabe>> AufgabenAsync(ClaimsPrincipal benutzer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(benutzer);
        if (!benutzer.IsInRole(Rollen.Admin) && !benutzer.HasClaim(Rechte.ClaimTyp, nameof(Recht.Finanzen)))
        {
            return [];
        }

        var einstellungen = await Finanzeinstellungsverwaltung.LadenOderNeuAsync(db, cancellationToken);
        if (einstellungen.BuchhaltungAb is null)
        {
            return await db.Set<Mietvertrag>().AnyAsync(cancellationToken)
                ? [new Aufgabe("Finanzen", "Buchhaltungsbeginn festlegen", "/verwaltung/finanzen/einstellungen", Dringlichkeit.Bald,
                    "Ab diesem Monat entstehen die Monatsmieten automatisch; ältere Rückstände als Saldovortrag erfassen.")]
                : [];
        }

        var offen = await konten.OffenePostenAsync(cancellationToken);
        if (offen.Count == 0)
        {
            return [];
        }

        var vertraege = offen.Select(p => p.MietvertragId).Distinct().Count();
        return [new Aufgabe("Finanzen", $"Rückstände: {offen.Sum(p => p.Offen).ToString("#,##0.00 €", CultureInfo.GetCultureInfo("de-DE"))} bei {vertraege} Mietverhältnis{(vertraege == 1 ? "" : "sen")}",
            "/verwaltung/finanzen/offene-posten", offen.Any(p => p.TageUeberfaellig > 30) ? Dringlichkeit.Dringend : Dringlichkeit.Bald,
            "Zahlungen erfassen bzw. Bankumsätze zuordnen, sonst mahnen.", offen.Min(p => p.Faellig))];
    }
}

/// <summary>Mieterkonten der Verträge einer Person für die DSGVO-Auskunft.</summary>
internal sealed class MieterkontoPersonendaten(MietHahnDbContext db) : IPersonendatenQuelle
{
    public string Abschnitt => "mieterkonten";

    public async Task<object?> DatenAsync(Guid personId, CancellationToken cancellationToken = default)
    {
        var vertragIds = await db.Set<Vertragspartei>().Where(p => p.PersonId == personId && p.Rolle == Parteirolle.Mieter).Select(p => p.MietvertragId).ToListAsync(cancellationToken);
        if (vertragIds.Count == 0)
        {
            return null;
        }

        var sollstellungen = await db.Set<Sollstellung>().AsNoTracking().Where(s => vertragIds.Contains(s.MietvertragId)).OrderBy(s => s.Faellig)
            .Select(s => new { s.MietvertragId, s.Faellig, s.Bezeichnung, s.Betrag }).ToListAsync(cancellationToken);
        var zahlungen = await db.Set<Zahlung>().AsNoTracking().Where(z => vertragIds.Contains(z.MietvertragId)).OrderBy(z => z.Datum)
            .Select(z => new { z.MietvertragId, z.Datum, z.Betrag, z.Verwendungszweck }).ToListAsync(cancellationToken);
        return sollstellungen.Count + zahlungen.Count == 0 ? null : new { sollstellungen, zahlungen };
    }
}
