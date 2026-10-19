using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Konten;
using MietHahn.Application.Objekte;
using MietHahn.Application.Personen;
using MietHahn.Application.Vertraege;
using MietHahn.Domain.Gemeinsam;
using MietHahn.Web.Tests.Konto;

namespace MietHahn.Web.Tests.Finanzen;

public class MieterkontoTests(PostgresFixture postgres) : KontoTestBasis(postgres)
{
    [Fact]
    public async Task Buchhaltung_einrichten_Mieten_entstehen_Zahlung_buchen_und_loeschen()
    {
        var (admin, _) = await AngemeldetAlsAsync(Rollen.Admin);
        using var _ = admin;
        // Einzug am 15. des Vormonats; die Uhr der App läuft in Echtzeit.
        var heute = Zeitzone.Heute(App.Uhr.GetUtcNow());
        var vormonat = new DateOnly(heute.Year, heute.Month, 1).AddMonths(-1);
        var vertrag = await VertragAsync(vormonat.AddDays(14));
        var tage = DateTime.DaysInMonth(vormonat.Year, vormonat.Month);

        var konto = await admin.LadenAsync($"/verwaltung/vertraege/{vertrag}/konto");
        Assert.Contains("Noch kein Buchhaltungsbeginn festgelegt", konto.Text, StringComparison.Ordinal);
        Assert.Contains("Buchhaltungsbeginn festlegen", (await admin.LadenAsync("/verwaltung")).TextMitStreaming, StringComparison.Ordinal);

        var einstellungen = await admin.AbsendenAsync(await admin.LadenAsync("/verwaltung/finanzen/einstellungen"), "finanzen", new Dictionary<string, string>
        {
            ["Daten.BuchhaltungAb"] = Iso(vormonat),
            ["Daten.Verrechnung"] = "Gesetzlich",
        });
        Assert.Contains("Gespeichert. 2 Sollstellungen angelegt oder angeglichen.", einstellungen.Text, StringComparison.Ordinal);

        konto = await admin.LadenAsync($"/verwaltung/vertraege/{vertrag}/konto");
        var anteilig = decimal.Round(650m * (tage - 14) / tage, 2, MidpointRounding.AwayFromZero);
        Assert.Contains($"Kaltmiete {vormonat:MM/yyyy} (anteilig {tage - 14}/{tage})", konto.Text, StringComparison.Ordinal);
        Assert.Contains(Geld(anteilig + 650m), konto.Dokument.QuerySelector("dl.angaben")!.TextContent, StringComparison.Ordinal);

        konto = await admin.AbsendenAsync(konto, "zahlung", new Dictionary<string, string>
        {
            ["Zahlung.Datum"] = Iso(heute),
            ["Zahlung.Betrag"] = "100.50",
            ["Zahlung.Verwendungszweck"] = "Miete",
        });
        Assert.Contains("Die Zahlung wurde gebucht.", konto.Text, StringComparison.Ordinal);
        Assert.Contains(Geld(anteilig + 650m - 100.50m), konto.Dokument.QuerySelector("dl.angaben")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("offen", konto.Text, StringComparison.Ordinal);

        // Monatsmieten haben keinen Löschknopf, die Zahlung schon.
        var knoepfe = konto.Formular("loeschen").QuerySelectorAll("button[name=Loeschen]").ToList();
        var knopf = Assert.Single(knoepfe);
        Assert.StartsWith("Zahlung:", knopf.GetAttribute("value"), StringComparison.Ordinal);
        konto = await admin.AbsendenAsync(konto, "loeschen", new Dictionary<string, string> { ["Loeschen"] = knopf.GetAttribute("value")! });
        Assert.Contains("Die Buchung wurde gelöscht.", konto.Text, StringComparison.Ordinal);
        Assert.Contains(Geld(anteilig + 650m), konto.Dokument.QuerySelector("dl.angaben")!.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Gutschrift_buchen_und_Offene_Posten_als_CSV()
    {
        var (admin, _) = await AngemeldetAlsAsync(Rollen.Admin);
        using var _ = admin;
        var heute = Zeitzone.Heute(App.Uhr.GetUtcNow());
        var vertrag = await VertragAsync(heute.AddMonths(-3));
        var konto = await admin.LadenAsync($"/verwaltung/vertraege/{vertrag}/konto");

        konto = await admin.AbsendenAsync(konto, "sollstellung", new Dictionary<string, string>
        {
            ["Sollstellung.Art"] = "Nachzahlung",
            ["Sollstellung.Faellig"] = Iso(heute.AddDays(-20)),
            ["Sollstellung.Betrag"] = "212.40",
            ["Sollstellung.Bezeichnung"] = "=Nachzahlung NK 2025",
        });
        Assert.Contains("Die Buchung wurde angelegt.", konto.Text, StringComparison.Ordinal);
        konto = await admin.AbsendenAsync(konto, "sollstellung", new Dictionary<string, string>
        {
            ["Sollstellung.Art"] = "Erstattung",
            ["Sollstellung.Faellig"] = Iso(heute.AddDays(-10)),
            ["Sollstellung.Betrag"] = "12.40",
        });
        Assert.Contains("Erstattung (Guthaben)", konto.Text, StringComparison.Ordinal);
        Assert.Contains(Geld(200m), konto.Dokument.QuerySelector("dl.angaben")!.TextContent, StringComparison.Ordinal);

        var liste = await admin.LadenAsync("/verwaltung/finanzen/offene-posten");
        Assert.Contains("=Nachzahlung NK 2025", liste.Text, StringComparison.Ordinal);
        Assert.Contains("Summe offen: " + Geld(200m), liste.Text, StringComparison.Ordinal);

        using var csv = await admin.Http.GetAsync(new Uri("/verwaltung/finanzen/offene-posten.csv", UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal("text/csv", csv.Content.Headers.ContentType!.MediaType);
        var inhalt = Encoding.UTF8.GetString(await csv.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        var zeilen = inhalt.TrimStart('﻿').Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("Mietobjekt;Mieter;Art;Forderung;Fällig;Betrag;Offen;Tage überfällig", zeilen[0]);
        Assert.Equal($"Lindenweg 3, Wohnung 1;Anna Schmidt;Nachzahlung (Abrechnung);'=Nachzahlung NK 2025;{heute.AddDays(-20):dd.MM.yyyy};212,40;200,00;20", zeilen[1]);
    }

    [Fact]
    public async Task Ohne_Recht_Finanzen_kein_Mieterkonto()
    {
        var vertrag = await VertragAsync(new DateOnly(2026, 1, 1));
        var (verwalter, _) = await AngemeldetAlsAsync(Rollen.Verwalter, [Recht.Stammdaten]);
        using var _ = verwalter;

        Assert.Equal("/konto/zugriff-verweigert", (await verwalter.LadenAsync($"/verwaltung/vertraege/{vertrag}/konto", weiterleitungenFolgen: false)).Weiterleitung!.AbsolutePath);
        Assert.NotEqual(HttpStatusCode.OK, (await verwalter.LadenAsync("/verwaltung/finanzen/offene-posten.csv", weiterleitungenFolgen: false)).Status);
        var details = await verwalter.LadenAsync($"/verwaltung/vertraege/{vertrag}");
        Assert.DoesNotContain("Mieterkonto", details.Text, StringComparison.Ordinal);
    }

    private static string Geld(decimal betrag) => betrag.ToString("#,##0.00 €", CultureInfo.GetCultureInfo("de-DE"));

    private static string Iso(DateOnly datum) => datum.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private async Task<Guid> VertragAsync(DateOnly beginn) =>
        await App.MitScopeAsync(async sp =>
        {
            var ct = TestContext.Current.CancellationToken;
            var objekte = sp.GetRequiredService<IObjektVerwaltung>();
            var objekt = (await objekte.AnlegenAsync(new ObjektDaten { Bezeichnung = "Lindenweg 3", Strasse = "Lindenweg 3", Plz = "50667", Ort = "Köln" }, ct)).Id!.Value;
            var wohnung = (await objekte.EinheitAnlegenAsync(objekt, new EinheitDaten { Bezeichnung = "Wohnung 1" }, ct)).Id!.Value;
            var mieter = (await sp.GetRequiredService<IPersonenVerwaltung>().AnlegenAsync(new PersonDaten { Vorname = "Anna", Nachname = "Schmidt" }, null, ct)).Id!.Value;
            return (await sp.GetRequiredService<IVertragsVerwaltung>().AnlegenAsync(new VertragAnlage { MietobjektId = wohnung, Beginn = beginn, MieterIds = [mieter], Kaltmiete = 650m }, ct)).Id!.Value;
        });
}
