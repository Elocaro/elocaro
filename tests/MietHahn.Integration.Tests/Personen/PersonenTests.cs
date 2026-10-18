using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Audit;
using MietHahn.Application.Datenschutz;
using MietHahn.Application.Gemeinsam;
using MietHahn.Application.Konten;
using MietHahn.Application.Personen;
using MietHahn.Domain.Gemeinsam;
using MietHahn.Domain.Personen;
using MietHahn.Infrastructure.Audit;
using MietHahn.Infrastructure.Persistenz;
using MietHahn.Integration.Tests.Infrastruktur;

namespace MietHahn.Integration.Tests.Personen;

public class PersonenTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateOnly Heute = DateOnly.FromDateTime(InfrastrukturUmgebung.Startzeit.UtcDateTime);

    private readonly TestBezuege bezuege = new();
    private InfrastrukturUmgebung umgebung = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() =>
        umgebung = await InfrastrukturUmgebung.ErstellenAsync(postgres, anpassen: s => s.AddSingleton<IPersonenbezug>(bezuege));

    public async ValueTask DisposeAsync() => await umgebung.DisposeAsync();

    [Fact]
    public async Task Mieter_mit_Anschrift_und_mehreren_IBANs_anlegen()
    {
        var id = await AnnaAsync();

        Pruefen(await MitAsync(v => v.BankverbindungHinzufuegenAsync(id, new BankverbindungEingabe { Iban = "DE89 3704 0044 0532 0130 00", Kontoinhaber = "Hans Schmidt" }, Ct)));
        var doppelt = await MitAsync(v => v.BankverbindungHinzufuegenAsync(id, new BankverbindungEingabe { Iban = "de02120300000000202051" }, Ct));

        var person = (await MitAsync(v => v.LadenAsync(id, Ct)))!;
        Assert.Equal("Dr. Anna Schmidt", person.Anzeigename);
        Assert.Equal("Lindenweg 3, 50667 Köln", person.Anschrift!.Einzeilig);
        Assert.Equal(["DE02 1203 0000 0000 2020 51", "DE89 3704 0044 0532 0130 00"], person.Bankverbindungen.Select(b => b.Iban));
        Assert.False(doppelt.Erfolgreich);
        Assert.Equal("Diese IBAN ist bereits hinterlegt.", doppelt.Fehler.Single());
    }

    [Fact]
    public async Task Oeffnen_der_Personenakte_wird_protokolliert()
    {
        var id = await AnnaAsync();

        await MitAsync(v => v.LadenAsync(id, Ct));

        await using var scope = umgebung.Services.CreateAsyncScope();
        var eintrag = await scope.ServiceProvider.GetRequiredService<MietHahnDbContext>().Set<AuditEintrag>()
            .SingleAsync(a => a.EntitaetId == id && a.Aktion == AuditAktion.Gelesen, Ct);
        Assert.Equal("Personenakte geöffnet", eintrag.Zweck);
    }

    [Theory]
    [InlineData("schmidt", 1)]
    [InlineData("anna@", 1)]
    [InlineData("0221", 1)]
    [InlineData("DE89370400440532013000", 1)] // IBAN ohne Leerzeichen findet die formatierte
    [InlineData("3704 0044", 1)]
    [InlineData("Sanitär", 1)]
    [InlineData("niemand", 0)]
    [InlineData("100%", 0)] // Platzhalter werden nicht als Muster verstanden
    public async Task Suche_ueber_Name_Kontakt_IBAN_und_Gewerk(string text, int treffer)
    {
        var anna = await AnnaAsync();
        Pruefen(await MitAsync(v => v.BankverbindungHinzufuegenAsync(anna, new BankverbindungEingabe { Iban = "DE89370400440532013000" }, Ct)));
        Pruefen(await MitAsync(v => v.AnlegenAsync(new PersonDaten { Nachname = "Klempner", Firma = "Rohr & Co.", Kontaktart = Kontaktart.Handwerker, Taetigkeit = "Sanitär" }, null, Ct)));

        var ergebnis = await MitAsync(v => v.SucheAsync(new PersonenSuche(text), Ct));

        Assert.Equal(treffer, ergebnis.Count);
    }

    [Fact]
    public async Task Filter_Privat_und_Kontakte_mit_Rollen_aus_den_Bezuegen()
    {
        var anna = await AnnaAsync();
        Pruefen(await MitAsync(v => v.AnlegenAsync(new PersonDaten { Nachname = "Stadtwerke", Kontaktart = Kontaktart.Versorger, Taetigkeit = "Wasser" }, null, Ct)));
        bezuege.Hinzufuegen(new Personenbezug(anna, "Mieter", "Wohnung 1", true, null));

        var privat = await MitAsync(v => v.SucheAsync(new PersonenSuche(Filter: Personenfilter.Privat), Ct));
        var kontakte = await MitAsync(v => v.SucheAsync(new PersonenSuche(Filter: Personenfilter.Kontakte), Ct));

        Assert.Equal(["Mieter"], Assert.Single(privat).Rollen);
        Assert.Equal("Stadtwerke", Assert.Single(kontakte).Anzeigename);
    }

    [Fact]
    public async Task Personen_mit_Bezug_koennen_nicht_geloescht_werden()
    {
        var anna = await AnnaAsync();
        bezuege.Hinzufuegen(new Personenbezug(anna, "Mieter", "Wohnung 1", false, Heute.AddYears(-1)));

        Assert.False((await MitAsync(v => v.LoeschenAsync(anna, Ct))).Erfolgreich);

        var interessent = Pruefen(await MitAsync(v => v.AnlegenAsync(new PersonDaten { Nachname = "Interessent" }, null, Ct)));
        Pruefen(await MitAsync(v => v.LoeschenAsync(interessent, Ct)));
        Assert.Null(await MitAsync(v => v.LadenAsync(interessent, Ct)));
    }

    [Fact]
    public async Task Zugang_laedt_ein_und_verknuepft_das_Konto()
    {
        var anna = await AnnaAsync();
        var ohneMail = Pruefen(await MitAsync(v => v.AnlegenAsync(new PersonDaten { Nachname = "Ohne Mail" }, null, Ct)));

        var ergebnis = await MitAsync(v => v.ZugangEinrichtenAsync(anna, Ct));

        Pruefen(ergebnis);
        var person = (await MitAsync(v => v.LadenAsync(anna, Ct)))!;
        Assert.Equal("anna@example.de", person.Zugang!.Email);
        Assert.Equal(Rollen.Mieter, person.Zugang.Rolle);
        Assert.False((await MitAsync(v => v.ZugangEinrichtenAsync(anna, Ct))).Erfolgreich);
        Assert.Equal("Für einen Zugang braucht die Person eine E-Mail-Adresse.", (await MitAsync(v => v.ZugangEinrichtenAsync(ohneMail, Ct))).Fehler.Single());

        // Eine zweite Person mit derselben Adresse darf das Konto nicht übernehmen.
        var zweite = Pruefen(await MitAsync(v => v.AnlegenAsync(new PersonDaten { Nachname = "Doppelt", Email = "anna@example.de" }, null, Ct)));
        Assert.False((await MitAsync(v => v.ZugangEinrichtenAsync(zweite, Ct))).Erfolgreich);
    }

    [Fact]
    public async Task Dsgvo_Auskunft_enthaelt_alle_gespeicherten_Daten()
    {
        var anna = await AnnaAsync();
        Pruefen(await MitAsync(v => v.BankverbindungHinzufuegenAsync(anna, new BankverbindungEingabe { Iban = "DE89370400440532013000", Kontoinhaber = "Hans Schmidt", Bic = "COBADEFFXXX" }, Ct)));
        var entfernt = (await MitAsync(v => v.LadenAsync(anna, Ct)))!.Bankverbindungen[0].Id;
        Pruefen(await MitAsync(v => v.BankverbindungEntfernenAsync(anna, entfernt, Ct)));
        Pruefen(await MitAsync(v => v.ZugangEinrichtenAsync(anna, Ct)));
        bezuege.Hinzufuegen(new Personenbezug(anna, "Mieter", "Wohnung 1, Lindenweg 3", true, null));

        var json = await AuskunftAsync(anna);

        var person = json.GetProperty("person");
        // Jede Eigenschaft der Entitäten ist enthalten, damit neue Felder nicht versehentlich fehlen.
        foreach (var name in Eigenschaften<Person>().Except(["Anschriften", "Bankverbindungen"]))
        {
            Assert.True(person.TryGetProperty(name, out _), $"person.{name} fehlt in der Auskunft");
        }

        var anschrift = person.GetProperty("anschriften")[0];
        Assert.All(Eigenschaften<PersonAnschrift>(), n => Assert.True(anschrift.TryGetProperty(n, out _), $"anschrift.{n} fehlt"));
        var konto = person.GetProperty("bankverbindungen")[0];
        Assert.All(Eigenschaften<Bankverbindung>(), n => Assert.True(konto.TryGetProperty(n, out _), $"bankverbindung.{n} fehlt"));
        Assert.Equal("DE89 3704 0044 0532 0130 00", konto.GetProperty("iban").GetString()); // im Klartext

        var text = json.GetRawText();
        Assert.Contains("1990-04-12", text, StringComparison.Ordinal);
        Assert.Contains("DE02 1203 0000 0000 2020 51", json.GetProperty("geloeschteEintraege").GetProperty("bankverbindungen")[0].GetProperty("iban").GetString()!, StringComparison.Ordinal);
        Assert.Equal("anna@example.de", json.GetProperty("benutzerkonto").GetProperty("email").GetString());
        Assert.Equal("Wohnung 1, Lindenweg 3", json.GetProperty("bezuege")[0].GetProperty("beschreibung").GetString());
        Assert.Contains(json.GetProperty("protokoll").EnumerateArray(), e => e.GetProperty("entitaetTyp").GetString() == nameof(Bankverbindung));
        Assert.Equal("Anna", person.GetProperty("vorname").GetString());
    }

    [Fact]
    public async Task Vorschlaege_nach_Aufbewahrungsfrist()
    {
        var aktiv = await AnnaAsync();
        var ehemalig = Pruefen(await MitAsync(v => v.AnlegenAsync(new PersonDaten { Nachname = "Ehemalig" }, null, Ct)));
        var nochInFrist = Pruefen(await MitAsync(v => v.AnlegenAsync(new PersonDaten { Nachname = "Noch in Frist" }, null, Ct)));
        var interessent = Pruefen(await MitAsync(v => v.AnlegenAsync(new PersonDaten { Nachname = "Interessent" }, null, Ct)));
        Pruefen(await MitAsync(v => v.AnlegenAsync(new PersonDaten { Nachname = "Stadtwerke", Kontaktart = Kontaktart.Versorger }, null, Ct)));
        bezuege.Hinzufuegen(new Personenbezug(aktiv, "Mieter", "Wohnung 1", true, null));
        bezuege.Hinzufuegen(new Personenbezug(ehemalig, "Mieter", "Wohnung 2", false, Heute.AddYears(-11)));
        bezuege.Hinzufuegen(new Personenbezug(nochInFrist, "Mieter", "Wohnung 3", false, Heute.AddYears(-3)));

        Assert.Equal([ehemalig], (await VorschlaegeAsync()).Select(v => v.PersonId));

        umgebung.Uhr.Advance(TimeSpan.FromDays(400));
        var spaeter = await VorschlaegeAsync();
        Assert.Contains(spaeter, v => v.PersonId == interessent && v.Grund.StartsWith("Kein Vertrag", StringComparison.Ordinal));
        Assert.DoesNotContain(spaeter, v => v.PersonId == aktiv || v.PersonId == nochInFrist);
        Assert.DoesNotContain(spaeter, v => v.Anzeigename == "Stadtwerke");
    }

    [Fact]
    public async Task Anonymisieren_entfernt_Daten_endgueltig_auch_aus_dem_Protokoll()
    {
        var anna = await AnnaAsync();
        var bank = (await MitAsync(v => v.LadenAsync(anna, Ct)))!.Bankverbindungen[0].Id;
        Pruefen(await MitAsync(v => v.BankverbindungEntfernenAsync(anna, bank, Ct)));
        bezuege.Hinzufuegen(new Personenbezug(anna, "Mieter", "Wohnung 1", true, null));
        Assert.False((await MitDatenschutzAsync(d => d.AnonymisierenAsync(anna, Ct))).Erfolgreich);
        bezuege.Leeren();
        bezuege.Hinzufuegen(new Personenbezug(anna, "Mieter", "Wohnung 1", false, Heute.AddYears(-11)));

        Pruefen(await MitDatenschutzAsync(d => d.AnonymisierenAsync(anna, Ct)));

        await using var scope = umgebung.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MietHahnDbContext>();
        var person = await db.Set<Person>().SingleAsync(p => p.Id == anna, Ct);
        Assert.True(person.IstAnonymisiert);
        Assert.False(await db.Set<PersonAnschrift>().IgnoreQueryFilters().AnyAsync(a => a.PersonId == anna, Ct));
        Assert.False(await db.Set<Bankverbindung>().IgnoreQueryFilters().AnyAsync(b => b.PersonId == anna, Ct));

        var protokoll = await db.Set<AuditEintrag>().Select(a => a.AenderungenJson).ToListAsync(Ct);
        Assert.DoesNotContain(protokoll, p => p.Contains("Schmidt", StringComparison.Ordinal) || p.Contains("Lindenweg", StringComparison.Ordinal) || p.Contains("anna@", StringComparison.Ordinal));
        Assert.Contains(await db.Set<AuditEintrag>().Where(a => a.EntitaetId == anna).Select(a => a.Zweck).ToListAsync(Ct), z => z == "Anonymisiert (DSGVO)");

        Assert.False((await MitAsync(v => v.AendernAsync(anna, new PersonDaten { Nachname = "Wieder da" }, Ct))).Erfolgreich);
        Assert.Empty(await MitAsync(v => v.SucheAsync(new PersonenSuche(), Ct)));
    }

    private async Task<Guid> AnnaAsync()
    {
        var id = Pruefen(await MitAsync(v => v.AnlegenAsync(
            new PersonDaten { Anrede = Anrede.Frau, Titel = "Dr.", Vorname = "Anna", Nachname = "Schmidt", Email = "anna@example.de", Telefon = "0221 123456", Geburtsdatum = new DateOnly(1990, 4, 12), Notizen = "Hat einen Hund" },
            new AnschriftEingabe { Ab = Heute, Strasse = "Lindenweg 3", Plz = "50667", Ort = "Köln" },
            Ct)));
        Pruefen(await MitAsync(v => v.BankverbindungHinzufuegenAsync(id, new BankverbindungEingabe { Iban = "DE02120300000000202051" }, Ct)));
        return id;
    }

    private static IEnumerable<string> Eigenschaften<T>() =>
        typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name));

    private async Task<JsonElement> AuskunftAsync(Guid id)
    {
        var bytes = await MitDatenschutzAsync(d => d.AuskunftAsync(id, Ct));
        return JsonDocument.Parse(bytes!).RootElement;
    }

    private Task<IReadOnlyList<Anonymisierungsvorschlag>> VorschlaegeAsync() => MitDatenschutzAsync(d => d.VorschlaegeAsync(Ct));

    private static Guid Pruefen(Ergebnis ergebnis)
    {
        Assert.True(ergebnis.Erfolgreich, string.Join(", ", ergebnis.Fehler));
        return ergebnis.Id!.Value;
    }

    private async Task<T> MitAsync<T>(Func<IPersonenVerwaltung, Task<T>> aktion)
    {
        await using var scope = umgebung.Services.CreateAsyncScope();
        return await aktion(scope.ServiceProvider.GetRequiredService<IPersonenVerwaltung>());
    }

    private async Task<T> MitDatenschutzAsync<T>(Func<IDatenschutz, Task<T>> aktion)
    {
        await using var scope = umgebung.Services.CreateAsyncScope();
        return await aktion(scope.ServiceProvider.GetRequiredService<IDatenschutz>());
    }

    private sealed class TestBezuege : IPersonenbezug
    {
        private readonly List<Personenbezug> liste = [];

        public void Hinzufuegen(Personenbezug bezug) => liste.Add(bezug);

        public void Leeren() => liste.Clear();

        public Task<IReadOnlyList<Personenbezug>> BezuegeAsync(IReadOnlyCollection<Guid> personIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Personenbezug>>(liste.Where(b => personIds.Contains(b.PersonId)).ToList());
    }
}
