using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MietHahn.Application.Audit;
using MietHahn.Application.Gemeinsam;
using MietHahn.Application.Konten;
using MietHahn.Application.Personen;
using MietHahn.Domain.Gemeinsam;
using MietHahn.Domain.Personen;
using MietHahn.Infrastructure.Gemeinsam;
using MietHahn.Infrastructure.Identitaet;
using MietHahn.Infrastructure.Persistenz;

namespace MietHahn.Infrastructure.Personen;

internal sealed class PersonenVerwaltung(
    MietHahnDbContext db,
    IEnumerable<IPersonenbezug> bezugsquellen,
    IZugriffsprotokoll zugriffe,
    IKontoverwaltung konten,
    UserManager<Benutzer> benutzerManager,
    TimeProvider uhr) : IPersonenVerwaltung
{
    private DateOnly Heute => Zeitzone.Heute(uhr.GetUtcNow());

    public async Task<IReadOnlyList<PersonUebersicht>> SucheAsync(PersonenSuche suche, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(suche);
        var abfrage = MitDetails();
        if (!suche.MitAnonymisierten)
        {
            abfrage = abfrage.Where(p => p.AnonymisiertAm == null);
        }

        abfrage = suche.Filter switch
        {
            Personenfilter.Privat => abfrage.Where(p => p.Kontaktart == Kontaktart.Keine),
            Personenfilter.Kontakte => abfrage.Where(p => p.Kontaktart != Kontaktart.Keine),
            _ => abfrage,
        };

        if (!string.IsNullOrWhiteSpace(suche.Text))
        {
            var muster = "%" + suche.Text.Trim().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
            var ibanMuster = "%" + Iban.Kompakt(suche.Text.Trim()) + "%";
            abfrage = abfrage.Where(p =>
                EF.Functions.ILike(p.Nachname, muster)
                || (p.Vorname != null && EF.Functions.ILike(p.Vorname, muster))
                || (p.Firma != null && EF.Functions.ILike(p.Firma, muster))
                || (p.Taetigkeit != null && EF.Functions.ILike(p.Taetigkeit, muster))
                || (p.Email != null && EF.Functions.ILike(p.Email, muster))
                || (p.Telefon != null && EF.Functions.ILike(p.Telefon, muster))
                || (p.Mobil != null && EF.Functions.ILike(p.Mobil, muster))
                || p.Bankverbindungen.Any(b => EF.Functions.ILike(b.Iban.Replace(" ", ""), ibanMuster)));
        }

        var personen = await abfrage.OrderBy(p => p.Nachname).ThenBy(p => p.Vorname).ToListAsync(cancellationToken);
        var bezuege = await BezuegeAsync(personen.Select(p => p.Id).ToList(), cancellationToken);
        return personen.Select(p => new PersonUebersicht(
            p.Id,
            p.Anzeigename,
            p.Sortiername,
            p.Kontaktart,
            p.Taetigkeit,
            p.Email,
            p.Telefon ?? p.Mobil,
            p.AnschriftAm(Heute)?.Ort,
            bezuege.Where(b => b.PersonId == p.Id).Select(b => b.Rolle).Distinct().ToList(),
            p.BenutzerId is not null,
            p.IstAnonymisiert)).ToList();
    }

    public async Task<PersonDetails?> LadenAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var person = await MitDetails().SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (person is null)
        {
            return null;
        }

        await zugriffe.ProtokollierenAsync(nameof(Person), id, "Personenakte geöffnet", cancellationToken);

        ZugangAnsicht? zugang = null;
        if (person.BenutzerId is { } benutzerId && await benutzerManager.FindByIdAsync(benutzerId.ToString()) is { } konto)
        {
            var rolle = (await benutzerManager.GetRolesAsync(konto)).FirstOrDefault() ?? "";
            zugang = new ZugangAnsicht(benutzerId, konto.Email ?? "", rolle, konto.Gesperrt);
        }

        return new PersonDetails(
            person.Id,
            Daten(person),
            person.Anzeigename,
            person.AnschriftAm(Heute) is { } aktuell ? new AnschriftStand(aktuell.Gueltigkeit, aktuell.Einzeilig) : null,
            person.Anschriften.OrderByDescending(a => a.Gueltigkeit.Von).Select(a => new AnschriftStand(a.Gueltigkeit, a.Einzeilig)).ToList(),
            person.Bankverbindungen.OrderBy(b => b.ErstelltAm).Select(b => new BankverbindungAnsicht(b.Id, b.Iban, b.Kontoinhaber, b.Bic)).ToList(),
            zugang,
            await BezuegeAsync([id], cancellationToken),
            person.AnonymisiertAm);
    }

    public Task<Ergebnis> AnlegenAsync(PersonDaten daten, AnschriftEingabe? anschrift = null, CancellationToken cancellationToken = default) =>
        Befehl.AusfuehrenAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(daten);
            var person = new Person(daten.Nachname.Trim(), Leer(daten.Vorname));
            Uebernehmen(person, daten);
            if (anschrift is not null && !string.IsNullOrWhiteSpace(anschrift.Strasse))
            {
                AnschriftSetzen(person, anschrift);
            }

            db.Add(person);
            await db.SaveChangesAsync(cancellationToken);
            return person.Id;
        });

    public Task<Ergebnis> AendernAsync(Guid id, PersonDaten daten, CancellationToken cancellationToken = default) =>
        MitPersonAsync(id, person =>
        {
            ArgumentNullException.ThrowIfNull(daten);
            person.Pruefen();
            Uebernehmen(person, daten);
        }, cancellationToken);

    public Task<Ergebnis> AnschriftSetzenAsync(Guid id, AnschriftEingabe eingabe, CancellationToken cancellationToken = default) =>
        MitPersonAsync(id, person => AnschriftSetzen(person, eingabe), cancellationToken);

    public Task<Ergebnis> BankverbindungHinzufuegenAsync(Guid id, BankverbindungEingabe eingabe, CancellationToken cancellationToken = default) =>
        MitPersonAsync(id, person =>
        {
            ArgumentNullException.ThrowIfNull(eingabe);
            person.BankverbindungHinzufuegen(eingabe.Iban, eingabe.Kontoinhaber, eingabe.Bic);
        }, cancellationToken);

    public Task<Ergebnis> BankverbindungEntfernenAsync(Guid id, Guid bankverbindungId, CancellationToken cancellationToken = default) =>
        MitPersonAsync(id, person =>
        {
            var bank = person.Bankverbindungen.SingleOrDefault(b => b.Id == bankverbindungId) ?? throw new NichtGefundenException();
            person.Pruefen();
            person.Bankverbindungen.Remove(bank);
        }, cancellationToken);

    public async Task<Ergebnis> ZugangEinrichtenAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var person = await db.Set<Person>().SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (person is null)
        {
            return Befehl.NichtGefunden;
        }

        if (person.BenutzerId is not null)
        {
            return Ergebnis.Fehlgeschlagen("Die Person hat bereits einen Zugang.");
        }

        if (string.IsNullOrWhiteSpace(person.Email))
        {
            return Ergebnis.Fehlgeschlagen("Für einen Zugang braucht die Person eine E-Mail-Adresse.");
        }

        Guid benutzerId;
        if (await benutzerManager.FindByEmailAsync(person.Email) is { } vorhanden)
        {
            if (await db.Set<Person>().AnyAsync(p => p.BenutzerId == vorhanden.Id, cancellationToken))
            {
                return Ergebnis.Fehlgeschlagen("Das Konto mit dieser E-Mail-Adresse gehört bereits zu einer anderen Person.");
            }

            benutzerId = vorhanden.Id;
        }
        else
        {
            var rolle = person.Kontaktart switch
            {
                Kontaktart.Hausmeister => Rollen.Hausmeister,
                Kontaktart.Handwerker => Rollen.Handwerker,
                _ => Rollen.Mieter,
            };
            var einladung = await konten.EinladenAsync(person.Email, person.Anzeigename, rolle, cancellationToken);
            if (!einladung.Erfolgreich)
            {
                return Ergebnis.Fehlgeschlagen([.. einladung.Fehler]);
            }

            benutzerId = einladung.BenutzerId!.Value;
        }

        person.BenutzerId = benutzerId;
        await db.SaveChangesAsync(cancellationToken);
        return Ergebnis.Ok(benutzerId);
    }

    public async Task<Ergebnis> LoeschenAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if ((await BezuegeAsync([id], cancellationToken)).Count > 0)
        {
            return Ergebnis.Fehlgeschlagen("Die Person ist mit Verträgen oder anderen Vorgängen verknüpft. Nach Ablauf der Aufbewahrungsfrist kann sie anonymisiert werden.");
        }

        return await MitPersonAsync(id, person => db.Remove(person), cancellationToken);
    }

    public async Task<IReadOnlyList<PersonAuswahl>> AuswahlAsync(CancellationToken cancellationToken = default)
    {
        var personen = await MitDetails().Where(p => p.AnonymisiertAm == null).OrderBy(p => p.Nachname).ThenBy(p => p.Vorname).ToListAsync(cancellationToken);
        return personen.Select(p => new PersonAuswahl(p.Id, p.Anzeigename, p.AnschriftAm(Heute)?.Ort)).ToList();
    }

    internal async Task<IReadOnlyList<Personenbezug>> BezuegeAsync(IReadOnlyCollection<Guid> personIds, CancellationToken cancellationToken)
    {
        var alle = new List<Personenbezug>();
        foreach (var quelle in bezugsquellen)
        {
            alle.AddRange(await quelle.BezuegeAsync(personIds, cancellationToken));
        }

        return alle;
    }

    private IQueryable<Person> MitDetails() =>
        db.Set<Person>().Include(p => p.Anschriften).Include(p => p.Bankverbindungen).AsSplitQuery();

    private Task<Ergebnis> MitPersonAsync(Guid id, Action<Person> aktion, CancellationToken cancellationToken) =>
        Befehl.AusfuehrenAsync(async () =>
        {
            var person = await MitDetails().SingleOrDefaultAsync(p => p.Id == id, cancellationToken) ?? throw new NichtGefundenException();
            aktion(person);
            await db.SaveChangesAsync(cancellationToken);
            return id;
        }, ueberlappung: "Für diesen Tag gibt es bereits eine Anschrift.");

    private static void AnschriftSetzen(Person person, AnschriftEingabe eingabe)
    {
        ArgumentNullException.ThrowIfNull(eingabe);
        person.AnschriftSetzen(
            eingabe.Ab ?? throw new RegelverletzungException("Bitte angeben, ab wann die Anschrift gilt."),
            eingabe.Strasse,
            eingabe.Plz,
            eingabe.Ort,
            eingabe.Zusatz,
            eingabe.Land ?? "Deutschland");
    }

    private static PersonDaten Daten(Person p) => new()
    {
        Anrede = p.Anrede,
        Titel = p.Titel,
        Vorname = p.Vorname,
        Nachname = p.Nachname,
        Firma = p.Firma,
        Kontaktart = p.Kontaktart,
        Taetigkeit = p.Taetigkeit,
        Geburtsdatum = p.Geburtsdatum,
        Email = p.Email,
        Telefon = p.Telefon,
        Mobil = p.Mobil,
        Notizen = p.Notizen,
    };

    private static void Uebernehmen(Person p, PersonDaten d)
    {
        p.Anrede = d.Anrede;
        p.Titel = Leer(d.Titel);
        p.Vorname = Leer(d.Vorname);
        p.Nachname = d.Nachname.Trim();
        p.Firma = Leer(d.Firma);
        p.Kontaktart = d.Kontaktart;
        p.Taetigkeit = Leer(d.Taetigkeit);
        p.Geburtsdatum = d.Geburtsdatum;
        p.Email = Leer(d.Email);
        p.Telefon = Leer(d.Telefon);
        p.Mobil = Leer(d.Mobil);
        p.Notizen = Leer(d.Notizen);
    }

    private static string? Leer(string? wert) => string.IsNullOrWhiteSpace(wert) ? null : wert.Trim();
}
