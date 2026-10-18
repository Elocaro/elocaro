using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MietHahn.Application.Audit;
using MietHahn.Application.Datenschutz;
using MietHahn.Application.Gemeinsam;
using MietHahn.Application.Personen;
using MietHahn.Domain.Gemeinsam;
using MietHahn.Domain.Personen;
using MietHahn.Infrastructure.Audit;
using MietHahn.Infrastructure.Gemeinsam;
using MietHahn.Infrastructure.Identitaet;
using MietHahn.Infrastructure.Persistenz;

namespace MietHahn.Infrastructure.Datenschutz;

internal sealed class Datenschutz(
    MietHahnDbContext db,
    IEnumerable<IPersonenbezug> bezugsquellen,
    IEnumerable<IPersonendatenQuelle> datenquellen,
    UserManager<Benutzer> benutzerManager,
    IAktuellerBenutzer aktuellerBenutzer,
    IOptions<DatenschutzOptionen> optionen,
    TimeProvider uhr) : IDatenschutz
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(), new ZeitraumJsonConverter() },
    };

    public async Task<byte[]?> AuskunftAsync(Guid personId, CancellationToken cancellationToken = default)
    {
        // Die Entitäten selbst serialisieren: neue Eigenschaften sind so automatisch in der Auskunft enthalten.
        var person = await db.Set<Person>().Include(p => p.Anschriften).Include(p => p.Bankverbindungen).AsSplitQuery()
            .AsNoTracking().SingleOrDefaultAsync(p => p.Id == personId, cancellationToken);
        if (person is null)
        {
            return null;
        }

        // Auch logisch gelöschte Einträge (z. B. entfernte IBAN) sind gespeicherte Daten.
        var geloeschteAnschriften = await db.Set<PersonAnschrift>().IgnoreQueryFilters().AsNoTracking().Where(a => a.PersonId == personId && a.Geloescht).ToListAsync(cancellationToken);
        var geloeschteKonten = await db.Set<Bankverbindung>().IgnoreQueryFilters().AsNoTracking().Where(b => b.PersonId == personId && b.Geloescht).ToListAsync(cancellationToken);
        var kindIds = person.Anschriften.Select(a => a.Id).Concat(person.Bankverbindungen.Select(b => b.Id))
            .Concat(geloeschteAnschriften.Select(a => a.Id)).Concat(geloeschteKonten.Select(b => b.Id)).Append(personId).ToList();
        var protokoll = await db.Set<AuditEintrag>().AsNoTracking()
            .Where(a => kindIds.Contains(a.EntitaetId))
            .OrderBy(a => a.Zeitpunkt)
            .Select(a => new { a.Zeitpunkt, a.Benutzer, a.EntitaetTyp, Aktion = a.Aktion.ToString(), a.Zweck, Aenderungen = a.AenderungenJson })
            .ToListAsync(cancellationToken);

        var auskunft = new Dictionary<string, object?>
        {
            ["erstelltAm"] = uhr.GetUtcNow(),
            ["hinweis"] = "Auskunft nach Art. 15 DSGVO über alle in MietHahn zu dieser Person gespeicherten Daten.",
            ["person"] = person,
            ["geloeschteEintraege"] = new { Anschriften = geloeschteAnschriften, Bankverbindungen = geloeschteKonten },
            ["benutzerkonto"] = await KontoAsync(person.BenutzerId),
            ["bezuege"] = await BezuegeAsync([personId], cancellationToken),
            // Änderungsprotokoll im Klartext (inkl. sonst maskierter Werte wie IBAN): die Person hat Anspruch darauf.
            ["protokoll"] = protokoll.Select(p => new { p.Zeitpunkt, p.Benutzer, p.EntitaetTyp, p.Aktion, p.Zweck, Aenderungen = JsonDocument.Parse(p.Aenderungen).RootElement }),
        };
        foreach (var quelle in datenquellen)
        {
            auskunft[quelle.Abschnitt] = await quelle.DatenAsync(personId, cancellationToken);
        }

        db.Add(new AuditEintrag(uhr.GetUtcNow(), aktuellerBenutzer.Kennung, nameof(Person), personId, AuditAktion.Gelesen, "[]", "DSGVO-Auskunft erstellt"));
        await db.SaveChangesAsync(cancellationToken);
        return JsonSerializer.SerializeToUtf8Bytes(auskunft, Json);
    }

    public async Task<IReadOnlyList<Anonymisierungsvorschlag>> VorschlaegeAsync(CancellationToken cancellationToken = default)
    {
        var heute = Zeitzone.Heute(uhr.GetUtcNow());
        var grenzeOhneBezug = uhr.GetUtcNow().AddMonths(-optionen.Value.OhneBezugMonate);

        // Geschäftskontakte (Handwerker, Versorger …) werden nicht vorgeschlagen.
        var personen = await db.Set<Person>().AsNoTracking()
            .Where(p => p.AnonymisiertAm == null && p.Kontaktart == Kontaktart.Keine)
            .Select(p => new { p.Id, p.Vorname, p.Nachname, p.Firma, p.Titel, p.BenutzerId, Stand = p.GeaendertAm ?? p.ErstelltAm })
            .ToListAsync(cancellationToken);
        var bezuege = await BezuegeAsync(personen.Select(p => p.Id).ToList(), cancellationToken);

        var vorschlaege = new List<Anonymisierungsvorschlag>();
        foreach (var person in personen)
        {
            var name = string.Join(' ', new[] { person.Titel, person.Vorname, person.Nachname }.Where(t => !string.IsNullOrWhiteSpace(t)));
            var eigene = bezuege.Where(b => b.PersonId == person.Id).ToList();
            if (eigene.Count == 0)
            {
                if (person.Stand < grenzeOhneBezug && person.BenutzerId is null)
                {
                    vorschlaege.Add(new(person.Id, name, $"Kein Vertrag oder Vorgang, seit über {optionen.Value.OhneBezugMonate} Monaten unverändert.", null));
                }

                continue;
            }

            if (eigene.Any(b => b.Aktiv))
            {
                continue;
            }

            var letzterBezug = eigene.Max(b => b.EndeAm);
            if (letzterBezug is { } ende && ende.AddYears(optionen.Value.AufbewahrungJahre) < heute)
            {
                vorschlaege.Add(new(person.Id, name,
                    string.Create(CultureInfo.InvariantCulture, $"Letzter Bezug endete am {ende:dd.MM.yyyy}, Aufbewahrungsfrist ({optionen.Value.AufbewahrungJahre} Jahre) abgelaufen."),
                    ende));
            }
        }

        return vorschlaege.OrderBy(v => v.LetzterBezug ?? DateOnly.MinValue).ToList();
    }

    public async Task<Ergebnis> AnonymisierenAsync(Guid personId, CancellationToken cancellationToken = default)
    {
        if ((await BezuegeAsync([personId], cancellationToken)).Any(b => b.Aktiv))
        {
            return Ergebnis.Fehlgeschlagen("Die Person hat noch aktive Verträge oder Vorgänge und kann nicht anonymisiert werden.");
        }

        var person = await db.Set<Person>().Include(p => p.Anschriften).Include(p => p.Bankverbindungen).AsSplitQuery()
            .SingleOrDefaultAsync(p => p.Id == personId, cancellationToken);
        if (person is null)
        {
            return Befehl.NichtGefunden;
        }

        if (person.IstAnonymisiert)
        {
            return Ergebnis.Fehlgeschlagen("Die Person ist bereits anonymisiert.");
        }

        await using var transaktion = await db.Database.BeginTransactionAsync(cancellationToken);
        person.Anonymisieren(uhr.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);

        // Logisch gelöschte Anschriften/Konten enthielten noch die Daten: endgültig entfernen,
        // ebenso die alten Werte im Änderungsprotokoll (das „Wer hat wann was geändert“ bleibt erhalten).
        var anschriften = db.Set<PersonAnschrift>().IgnoreQueryFilters().Where(a => a.PersonId == personId);
        var konten = db.Set<Bankverbindung>().IgnoreQueryFilters().Where(b => b.PersonId == personId);
        var ids = await anschriften.Select(a => a.Id).Concat(konten.Select(b => b.Id)).ToListAsync(cancellationToken);
        ids.Add(personId);
        await anschriften.ExecuteDeleteAsync(cancellationToken);
        await konten.ExecuteDeleteAsync(cancellationToken);
        await db.Set<AuditEintrag>()
            .Where(a => ids.Contains(a.EntitaetId) && a.Aktion != AuditAktion.Gelesen)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.AenderungenJson, "[]"), cancellationToken);
        db.Add(new AuditEintrag(uhr.GetUtcNow(), aktuellerBenutzer.Kennung, nameof(Person), personId, AuditAktion.Geaendert, "[]", "Anonymisiert (DSGVO)"));
        await db.SaveChangesAsync(cancellationToken);
        await transaktion.CommitAsync(cancellationToken);
        return Ergebnis.Ok(personId);
    }

    private async Task<object?> KontoAsync(Guid? benutzerId)
    {
        if (benutzerId is null || await benutzerManager.FindByIdAsync(benutzerId.Value.ToString()) is not { } konto)
        {
            return null;
        }

        return new
        {
            konto.Email,
            konto.Anzeigename,
            Rollen = await benutzerManager.GetRolesAsync(konto),
            konto.ErstelltAm,
            konto.LetzteAnmeldungAm,
            ZweiFaktorAktiv = konto.TwoFactorEnabled,
            konto.Gesperrt,
        };
    }

    private async Task<List<Personenbezug>> BezuegeAsync(IReadOnlyCollection<Guid> personIds, CancellationToken cancellationToken)
    {
        var alle = new List<Personenbezug>();
        foreach (var quelle in bezugsquellen)
        {
            alle.AddRange(await quelle.BezuegeAsync(personIds, cancellationToken));
        }

        return alle;
    }
}
