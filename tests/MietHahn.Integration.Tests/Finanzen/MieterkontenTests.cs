using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Aufgaben;
using MietHahn.Application.Finanzen;
using MietHahn.Application.Gemeinsam;
using MietHahn.Application.Konten;
using MietHahn.Application.Objekte;
using MietHahn.Application.Personen;
using MietHahn.Application.Vertraege;
using MietHahn.Domain.Finanzen;
using MietHahn.Domain.Vertraege;
using MietHahn.Infrastructure.Persistenz;
using MietHahn.Integration.Tests.Infrastruktur;

namespace MietHahn.Integration.Tests.Finanzen;

public class MieterkontenTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly ClaimsPrincipal Admin = new(new ClaimsIdentity([new Claim(ClaimTypes.Role, Rollen.Admin)], "test"));

    private InfrastrukturUmgebung umgebung = null!;
    private Guid wohnung;
    private Guid vertrag;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        umgebung = await InfrastrukturUmgebung.ErstellenAsync(postgres);
        await using var scope = umgebung.Services.CreateAsyncScope();
        var objekte = scope.ServiceProvider.GetRequiredService<IObjektVerwaltung>();
        var haus = Pruefen(await objekte.AnlegenAsync(new ObjektDaten { Bezeichnung = "Lindenweg 3", Strasse = "Lindenweg 3", Plz = "50667", Ort = "Köln" }, Ct));
        wohnung = Pruefen(await objekte.EinheitAnlegenAsync(haus, new EinheitDaten { Bezeichnung = "Wohnung 1" }, Ct));
        var anna = Pruefen(await scope.ServiceProvider.GetRequiredService<IPersonenVerwaltung>().AnlegenAsync(new PersonDaten { Vorname = "Anna", Nachname = "Schmidt" }, null, Ct));

        // Einzug am 15.03.2026
        vertrag = Pruefen(await scope.ServiceProvider.GetRequiredService<IVertragsVerwaltung>().AnlegenAsync(new VertragAnlage
        {
            MietobjektId = wohnung,
            Beginn = new DateOnly(2026, 3, 15),
            MieterIds = [anna],
            Kaltmiete = 620m,
            BetriebskostenVorauszahlung = 150m,
        }, Ct));
    }

    public async ValueTask DisposeAsync() => await umgebung.DisposeAsync();

    [Fact]
    public async Task Monatsmieten_anteilig_ab_Einzug_und_ohne_Duplikate_bei_erneutem_Lauf()
    {
        await BuchhaltungAbAsync(new DateOnly(2026, 1, 1));
        Jetzt(2026, 5, 10);

        Assert.Equal(6, await MitAsync(k => k.SollstellungenErzeugenAsync(null, Ct)));
        Assert.Equal(0, await MitAsync(k => k.SollstellungenErzeugenAsync(null, Ct)));

        var konto = (await MitAsync(k => k.LadenAsync(vertrag, Ct)))!;
        var maerz = konto.Buchungen.Where(b => b.Text.Contains("03/2026", StringComparison.Ordinal)).ToList();
        Assert.Equal([340.00m, 82.26m], maerz.Select(b => b.Soll)); // 17/31
        Assert.All(maerz, b => Assert.Equal(new DateOnly(2026, 3, 15), b.Datum)); // fällig frühestens am Einzugstag
        Assert.Equal("Kaltmiete 03/2026 (anteilig 17/31)", maerz[0].Text);
        Assert.Equal(new DateOnly(2026, 4, 7), konto.Buchungen.First(b => b.Text.StartsWith("Kaltmiete 04", StringComparison.Ordinal)).Datum); // 3. Werktag nach Karfreitag und Ostermontag
        Assert.Equal(new DateOnly(2026, 5, 6), konto.Buchungen[^1].Datum); // 1.5. ist Feiertag
        Assert.Equal(340m + 82.26m + 2 * 770m, konto.Saldo);
        Assert.Equal(konto.Saldo, konto.Faellig);
    }

    [Fact]
    public async Task Vertragsaenderungen_werden_nachgezogen()
    {
        await BuchhaltungAbAsync(new DateOnly(2026, 1, 1));
        Jetzt(2026, 7, 10);
        await MitAsync(k => k.SollstellungenErzeugenAsync(null, Ct));

        // Rückwirkende Mieterhöhung ab 01.06. und Auszug zum 15.07.
        await using (var scope = umgebung.Services.CreateAsyncScope())
        {
            var vertraege = scope.ServiceProvider.GetRequiredService<IVertragsVerwaltung>();
            Pruefen(await vertraege.BestandteilSetzenAsync(vertrag, new BestandteilEingabe { Art = Bestandteilart.Kaltmiete, Ab = new DateOnly(2026, 6, 1), Betrag = 650m }, Ct));
            Pruefen(await vertraege.AuszugAsync(vertrag, new AuszugEingabe { Ende = new DateOnly(2026, 7, 15) }, Ct));
        }

        Assert.Equal(3, await MitAsync(k => k.SollstellungenErzeugenAsync(vertrag, Ct))); // Kalt 06 und 07, BK 07

        var konto = (await MitAsync(k => k.LadenAsync(vertrag, Ct)))!;
        Assert.Equal(650m, konto.Buchungen.Single(b => b.Text == "Kaltmiete 06/2026").Soll);
        Assert.Equal(decimal.Round(650m * 15 / 31, 2), konto.Buchungen.Single(b => b.Text.StartsWith("Kaltmiete 07/2026", StringComparison.Ordinal)).Soll);

        // Späterer Buchhaltungsbeginn entfernt frühere Monatsmieten.
        await BuchhaltungAbAsync(new DateOnly(2026, 6, 1));
        await MitAsync(k => k.SollstellungenErzeugenAsync(null, Ct));
        Assert.DoesNotContain((await MitAsync(k => k.LadenAsync(vertrag, Ct)))!.Buchungen, b => b.Text.Contains("05/2026", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Ohne_Buchhaltungsbeginn_keine_Monatsmieten_aber_Hinweis_im_Dashboard()
    {
        Jetzt(2026, 5, 10);

        Assert.Equal(0, await MitAsync(k => k.SollstellungenErzeugenAsync(null, Ct)));

        var aufgabe = Assert.Single(await FinanzAufgabenAsync());
        Assert.Equal("Buchhaltungsbeginn festlegen", aufgabe.Titel);
    }

    [Fact]
    public async Task Mieterkonto_zeigt_gezahlte_Vorauszahlungen_je_Jahr_und_Zufluss()
    {
        await BuchhaltungAbAsync(new DateOnly(2026, 11, 1));
        Jetzt(2027, 2, 10);
        await MitAsync(k => k.SollstellungenErzeugenAsync(null, Ct));

        // November pünktlich, Dezember erst im Januar, Januar zur Hälfte, Februar offen
        await ZahlungAsync(new DateOnly(2026, 11, 3), 770m);
        await ZahlungAsync(new DateOnly(2027, 1, 4), 770m);
        await ZahlungAsync(new DateOnly(2027, 1, 20), 385m);

        var konto = (await MitAsync(k => k.LadenAsync(vertrag, Ct)))!;

        var bk2026 = konto.Jahreswerte.Single(w => w.Jahr == 2026 && w.Bestandteil == Bestandteilart.BetriebskostenVorauszahlung);
        Assert.Equal((300m, 300m, 150m), (bk2026.Soll, bk2026.Getilgt, bk2026.Zufluss));
        var bk2027 = konto.Jahreswerte.Single(w => w.Jahr == 2027 && w.Bestandteil == Bestandteilart.BetriebskostenVorauszahlung);
        Assert.Equal((300m, 75m, 225m), (bk2027.Soll, bk2027.Getilgt, bk2027.Zufluss));
        Assert.Equal(385m + 770m, konto.Saldo);
        Assert.Equal(konto.Saldo, konto.Buchungen[^1].Saldo);

        var offen = await MitAsync(k => k.OffenePostenAsync(Ct));
        Assert.Equal(385m + 770m, offen.Sum(p => p.Offen));
        Assert.Equal(["Kaltmiete 01/2027", "Betriebskostenvorauszahlung 01/2027", "Kaltmiete 02/2027", "Betriebskostenvorauszahlung 02/2027"], offen.Select(p => p.Text));
        Assert.Equal("Lindenweg 3, Wohnung 1", offen[0].Mietobjekt);
        Assert.Equal("Anna Schmidt", offen[0].Mieter);
        Assert.Equal(new DateOnly(2027, 2, 10).DayNumber - new DateOnly(2027, 1, 6).DayNumber, offen[0].TageUeberfaellig);

        var aufgabe = Assert.Single(await FinanzAufgabenAsync());
        Assert.StartsWith("Rückstände: 1.155,00 € bei 1 Mietverhältnis", aufgabe.Titel, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Mahngebuehr_wird_nach_gesetzlicher_Reihenfolge_zuerst_getilgt()
    {
        await BuchhaltungAbAsync(new DateOnly(2026, 4, 1));
        Jetzt(2026, 4, 20);
        await MitAsync(k => k.SollstellungenErzeugenAsync(null, Ct));
        var gebuehr = Pruefen(await MitAsync(k => k.SollstellungAnlegenAsync(vertrag, new SollstellungEingabe { Art = Postenart.Mahngebuehr, Faellig = new DateOnly(2026, 4, 15), Betrag = 5m }, Ct)));
        await ZahlungAsync(new DateOnly(2026, 4, 18), 770m);

        var konto = (await MitAsync(k => k.LadenAsync(vertrag, Ct)))!;
        Assert.Equal(0m, konto.Buchungen.Single(b => b.Id == gebuehr).Offen);
        Assert.Equal(5m, konto.Faellig);
        Assert.Equal("Mahngebühr", konto.Buchungen.Single(b => b.Id == gebuehr).Text);

        await using (var scope = umgebung.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IFinanzeinstellungen>().SpeichernAsync(new FinanzeinstellungenDaten { BuchhaltungAb = new DateOnly(2026, 4, 1), Verrechnung = Verrechnungsreihenfolge.MieteZuerst }, Ct);
        }

        konto = (await MitAsync(k => k.LadenAsync(vertrag, Ct)))!;
        Assert.Equal(5m, konto.Buchungen.Single(b => b.Id == gebuehr).Offen);
    }

    [Fact]
    public async Task Erstattung_und_Auszahlung_gleichen_sich_aus()
    {
        await BuchhaltungAbAsync(new DateOnly(2026, 4, 1));
        Jetzt(2026, 4, 20);
        await MitAsync(k => k.SollstellungenErzeugenAsync(null, Ct));
        await ZahlungAsync(new DateOnly(2026, 4, 3), 770m);
        var erstattung = Pruefen(await MitAsync(k => k.SollstellungAnlegenAsync(vertrag, new SollstellungEingabe { Art = Postenart.Erstattung, Faellig = new DateOnly(2026, 4, 10), Betrag = 120m, Bezeichnung = "Guthaben NK 2025" }, Ct)));

        var konto = (await MitAsync(k => k.LadenAsync(vertrag, Ct)))!;
        Assert.Equal(-120m, konto.Saldo);
        Assert.Equal(120m, konto.Buchungen.Single(b => b.Id == erstattung).Haben);

        Pruefen(await MitAsync(k => k.ZahlungErfassenAsync(vertrag, new ZahlungEingabe { Datum = new DateOnly(2026, 4, 15), Betrag = 120m, Auszahlung = true, Verwendungszweck = "Guthaben überwiesen" }, Ct)));
        konto = (await MitAsync(k => k.LadenAsync(vertrag, Ct)))!;
        Assert.Equal(0m, konto.Saldo);
        Assert.Equal("Auszahlung: Guthaben überwiesen", konto.Buchungen[^1].Text);
        Assert.Equal(120m, konto.Buchungen[^1].Soll);
        Assert.Empty(await MitAsync(k => k.OffenePostenAsync(Ct)));
    }

    [Fact]
    public async Task Loeschen_nur_fuer_manuelle_Buchungen_und_Bestimmung_entfaellt()
    {
        await BuchhaltungAbAsync(new DateOnly(2026, 4, 1));
        Jetzt(2026, 4, 20);
        await MitAsync(k => k.SollstellungenErzeugenAsync(null, Ct));
        var konto = (await MitAsync(k => k.LadenAsync(vertrag, Ct)))!;
        var miete = konto.Buchungen.First(b => b.Automatisch).Id;

        var abgelehnt = await MitAsync(k => k.SollstellungLoeschenAsync(miete, Ct));
        Assert.False(abgelehnt.Erfolgreich);
        Assert.Contains("ergeben sich aus dem Vertrag", abgelehnt.Fehler.Single(), StringComparison.Ordinal);

        var kaution = Pruefen(await MitAsync(k => k.SollstellungAnlegenAsync(vertrag, new SollstellungEingabe { Art = Postenart.Kaution, Faellig = new DateOnly(2026, 3, 15), Betrag = 1860m }, Ct)));
        var zahlung = Pruefen(await MitAsync(k => k.ZahlungErfassenAsync(vertrag, new ZahlungEingabe { Datum = new DateOnly(2026, 4, 1), Betrag = 1860m, SollstellungId = kaution }, Ct)));
        Assert.Equal(0m, (await MitAsync(k => k.LadenAsync(vertrag, Ct)))!.Buchungen.Single(b => b.Id == kaution).Offen);

        Pruefen(await MitAsync(k => k.SollstellungLoeschenAsync(kaution, Ct)));
        await using (var scope = umgebung.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MietHahnDbContext>();
            Assert.Null((await db.Set<Zahlung>().SingleAsync(z => z.Id == zahlung, Ct)).SollstellungId);
        }

        Pruefen(await MitAsync(k => k.ZahlungLoeschenAsync(zahlung, Ct)));
        Assert.Equal(770m, (await MitAsync(k => k.LadenAsync(vertrag, Ct)))!.Saldo);

        var fremd = await MitAsync(k => k.ZahlungErfassenAsync(vertrag, new ZahlungEingabe { Datum = new DateOnly(2026, 4, 1), Betrag = 10m, SollstellungId = Guid.CreateVersion7() }, Ct));
        Assert.False(fremd.Erfolgreich);
    }

    private void Jetzt(int jahr, int monat, int tag) => umgebung.Uhr.SetUtcNow(new DateTimeOffset(jahr, monat, tag, 10, 0, 0, TimeSpan.Zero));

    private async Task BuchhaltungAbAsync(DateOnly ab)
    {
        await using var scope = umgebung.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IFinanzeinstellungen>().SpeichernAsync(new FinanzeinstellungenDaten { BuchhaltungAb = ab }, Ct);
    }

    private async Task ZahlungAsync(DateOnly datum, decimal betrag) =>
        Pruefen(await MitAsync(k => k.ZahlungErfassenAsync(vertrag, new ZahlungEingabe { Datum = datum, Betrag = betrag }, Ct)));

    private async Task<IReadOnlyList<Aufgabe>> FinanzAufgabenAsync()
    {
        await using var scope = umgebung.Services.CreateAsyncScope();
        var quelle = scope.ServiceProvider.GetServices<IAufgabenQuelle>().Single(q => q.GetType().Name == "FinanzAufgaben");
        return await quelle.AufgabenAsync(Admin, Ct);
    }

    private static Guid Pruefen(Ergebnis ergebnis)
    {
        Assert.True(ergebnis.Erfolgreich, string.Join(", ", ergebnis.Fehler));
        return ergebnis.Id!.Value;
    }

    private async Task<T> MitAsync<T>(Func<IMieterkonten, Task<T>> aktion)
    {
        await using var scope = umgebung.Services.CreateAsyncScope();
        return await aktion(scope.ServiceProvider.GetRequiredService<IMieterkonten>());
    }
}
