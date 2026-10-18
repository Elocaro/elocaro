using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Konten;
using MietHahn.Domain.Kommunikation;
using MietHahn.Infrastructure.Persistenz;
using MietHahn.Web.Tests.Konto;

namespace MietHahn.Web.Tests.Personen;

public class PersonenTests(PostgresFixture postgres) : KontoTestBasis(postgres)
{
    // Personen ohne Bezug sofort vorschlagen, sobald sie nicht mehr ganz neu sind (statt nach 12 Monaten).
    protected override IDictionary<string, string?> Konfiguration => new Dictionary<string, string?> { ["Datenschutz:OhneBezugMonate"] = "0" };

    private static readonly Dictionary<string, string> Anna = new()
    {
        ["Daten.Anrede"] = "Frau",
        ["Daten.Vorname"] = "Anna",
        ["Daten.Nachname"] = "Schmidt",
        ["Daten.Email"] = "anna@example.de",
        ["Daten.Telefon"] = "0221 123456",
        ["Anschrift.Strasse"] = "Altweg 1",
        ["Anschrift.Plz"] = "50667",
        ["Anschrift.Ort"] = "Köln",
    };

    [Fact]
    public async Task Mieterin_anlegen_suchen_und_pflegen()
    {
        var (admin, _) = await AngemeldetAlsAsync(Rollen.Admin);
        using var _ = admin;

        var person = await admin.AbsendenAsync(await admin.LadenAsync("/verwaltung/personen/neu"), "person", Anna);
        Assert.Matches("^/verwaltung/personen/[0-9a-f-]{36}$", person.Pfad);
        Assert.Contains("Die Person wurde angelegt.", person.Text, StringComparison.Ordinal);
        Assert.Contains("Altweg 1, 50667 Köln", person.Text, StringComparison.Ordinal);

        // Mehrere IBANs, eine ungültige
        var falsch = await admin.AbsendenAsync(person, "iban", new Dictionary<string, string> { ["Bank.Iban"] = "DE02120300000000202052" });
        Assert.Contains("Bitte eine gültige IBAN angeben", falsch.Text, StringComparison.Ordinal);
        person = await admin.AbsendenAsync(person, "iban", new Dictionary<string, string> { ["Bank.Iban"] = "de02120300000000202051" });
        person = await admin.AbsendenAsync(person, "iban", new Dictionary<string, string> { ["Bank.Iban"] = "DE89370400440532013000", ["Bank.Kontoinhaber"] = "Hans Schmidt" });
        Assert.Contains("DE02 1203 0000 0000 2020 51", person.Text, StringComparison.Ordinal);
        Assert.Contains("Hans Schmidt", person.Text, StringComparison.Ordinal);

        // Jede Zeile hat einen eigenen Knopf im gemeinsamen Formular; der geklickte sendet seine Id.
        var ersteIban = person.Dokument.QuerySelector("button[name=EntfernenId]")!.GetAttribute("value")!;
        person = await admin.AbsendenAsync(person, "iban-entfernen", new Dictionary<string, string> { ["EntfernenId"] = ersteIban });
        Assert.Contains("Die IBAN wurde entfernt.", person.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("DE02 1203", person.Text, StringComparison.Ordinal);

        // Einzug: neue Anschrift, Voranschrift bleibt im Verlauf
        person = await admin.AbsendenAsync(person, "anschrift", new Dictionary<string, string>
        {
            ["Anschrift.Ab"] = MietHahn.Domain.Gemeinsam.Zeitzone.Heute(App.Uhr.GetUtcNow()).AddDays(1).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            ["Anschrift.Strasse"] = "Lindenweg 3",
            ["Anschrift.Plz"] = "50667",
            ["Anschrift.Ort"] = "Köln",
            ["Anschrift.Zusatz"] = "Wohnung 1",
        });
        Assert.Contains("Die Anschrift wurde gespeichert.", person.Text, StringComparison.Ordinal);
        Assert.Contains("Frühere und künftige Anschriften", person.Text, StringComparison.Ordinal);
        Assert.Contains("Wohnung 1, Lindenweg 3, 50667 Köln", person.Text, StringComparison.Ordinal); // ab morgen
        App.Uhr.Advance(TimeSpan.FromDays(1));
        Assert.Contains("Wohnung 1, Lindenweg 3, 50667 Köln (seit", (await admin.LadenAsync(person.Pfad)).Text, StringComparison.Ordinal);

        Assert.Contains("Schmidt, Anna", (await admin.LadenAsync("/verwaltung/personen?q=3704")).Text, StringComparison.Ordinal);
        Assert.Contains("Keine Treffer für „Meier“", (await admin.LadenAsync("/verwaltung/personen?q=Meier")).Text, StringComparison.Ordinal);
        Assert.Contains("Keine Personen in dieser Auswahl.", (await admin.LadenAsync("/verwaltung/personen?filter=Kontakte")).Text, StringComparison.Ordinal);
        Assert.Contains("Schmidt, Anna", (await admin.LadenAsync("/verwaltung/personen?filter=Unsinn")).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Handwerker_erhaelt_Zugang_mit_passender_Rolle()
    {
        var (admin, _) = await AngemeldetAlsAsync(Rollen.Admin);
        using var _ = admin;
        var person = await admin.AbsendenAsync(await admin.LadenAsync("/verwaltung/personen/neu"), "person", new Dictionary<string, string>
        {
            ["Daten.Nachname"] = "Klempner",
            ["Daten.Firma"] = "Rohr & Co.",
            ["Daten.Kontaktart"] = "Handwerker",
            ["Daten.Taetigkeit"] = "Sanitär",
            ["Daten.Email"] = "rohr@example.de",
        });
        Assert.Contains("Handwerker (Sanitär)", person.Text, StringComparison.Ordinal);

        person = await admin.AbsendenAsync(person, "zugang");

        Assert.Contains("Der Zugang wurde eingerichtet.", person.Text, StringComparison.Ordinal);
        Assert.Contains("rohr@example.de (Handwerker)", person.Text, StringComparison.Ordinal);
        var mail = await App.MitScopeAsync(sp => sp.GetRequiredService<MietHahnDbContext>().Set<AusgehendeMail>().SingleAsync(TestContext.Current.CancellationToken));
        Assert.Equal(["rohr@example.de"], mail.An);
        Assert.Contains("Zugang", (await admin.LadenAsync("/verwaltung/personen?filter=Kontakte")).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dsgvo_Auskunft_als_Download()
    {
        var (admin, _) = await AngemeldetAlsAsync(Rollen.Admin);
        using var _ = admin;
        var person = await admin.AbsendenAsync(await admin.LadenAsync("/verwaltung/personen/neu"), "person", Anna);
        var link = person.Dokument.QuerySelectorAll("a").Single(a => a.TextContent == "DSGVO-Auskunft").GetAttribute("href")!;

        using var antwort = await admin.Http.GetAsync(new Uri(link, UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        Assert.Equal("application/json", antwort.Content.Headers.ContentType!.MediaType);
        Assert.StartsWith("dsgvo-auskunft-", antwort.Content.Headers.ContentDisposition!.FileNameStar ?? antwort.Content.Headers.ContentDisposition.FileName, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(await antwort.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Schmidt", json.RootElement.GetProperty("person").GetProperty("nachname").GetString());
        Assert.Equal("Altweg 1", json.RootElement.GetProperty("person").GetProperty("anschriften")[0].GetProperty("strasse").GetString());
    }

    [Fact]
    public async Task Vorschlag_nach_Frist_und_Anonymisieren()
    {
        var (admin, _) = await AngemeldetAlsAsync(Rollen.Admin);
        using var _ = admin;
        var person = await admin.AbsendenAsync(await admin.LadenAsync("/verwaltung/personen/neu"), "person", Anna);
        var pfad = person.Pfad;

        App.Uhr.Advance(TimeSpan.FromMinutes(1));
        var vorschlaege = await admin.LadenAsync("/verwaltung/datenschutz");
        Assert.True(vorschlaege.Text.Contains("Anna Schmidt", StringComparison.Ordinal), vorschlaege.Text);

        var knopf = vorschlaege.Dokument.QuerySelector("button[name=PersonId]")!.GetAttribute("value")!;
        var danach = await admin.AbsendenAsync(vorschlaege, "anonymisieren", new Dictionary<string, string> { ["PersonId"] = knopf });
        Assert.Contains("Die Person wurde anonymisiert.", danach.Text, StringComparison.Ordinal);

        var akte = await admin.LadenAsync(pfad);
        Assert.DoesNotContain("Schmidt", akte.Text, StringComparison.Ordinal);
        Assert.Contains("wurden am", akte.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Bearbeiten", akte.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ohne_Recht_Stammdaten_kein_Zugriff_auf_Personen_und_Auskunft()
    {
        var (admin, _) = await AngemeldetAlsAsync(Rollen.Admin);
        var person = await admin.AbsendenAsync(await admin.LadenAsync("/verwaltung/personen/neu"), "person", Anna);
        admin.Dispose();
        var (verwalter, _) = await AngemeldetAlsAsync(Rollen.Verwalter, [Recht.Finanzen]);
        using var _ = verwalter;

        Assert.Equal("/konto/zugriff-verweigert", (await verwalter.LadenAsync("/verwaltung/personen", weiterleitungenFolgen: false)).Weiterleitung!.AbsolutePath);
        Assert.Equal("/konto/zugriff-verweigert", (await verwalter.LadenAsync(person.Pfad + "/auskunft", weiterleitungenFolgen: false)).Weiterleitung!.AbsolutePath);
        Assert.Equal("/konto/zugriff-verweigert", (await verwalter.LadenAsync("/verwaltung/datenschutz", weiterleitungenFolgen: false)).Weiterleitung!.AbsolutePath);
    }
}
