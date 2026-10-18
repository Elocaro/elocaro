using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Konten;
using MietHahn.Application.Objekte;
using MietHahn.Application.Personen;
using MietHahn.Domain.Gemeinsam;
using MietHahn.Web.Tests.Konto;

namespace MietHahn.Web.Tests.Vertraege;

public class VertraegeTests(PostgresFixture postgres) : KontoTestBasis(postgres)
{
    private DateOnly Heute => Zeitzone.Heute(App.Uhr.GetUtcNow());

    private static string Iso(DateOnly datum) => datum.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    [Fact]
    public async Task Vertrag_anlegen_Miete_erhoehen_kuendigen_und_aufheben()
    {
        var (admin, _) = await AngemeldetAlsAsync(Rollen.Admin);
        using var _ = admin;
        var (objekt, wohnung, anna, ben) = await StammdatenAsync();
        var beginn = new DateOnly(Heute.Year, Heute.Month, 1);

        var neu = await admin.LadenAsync($"/verwaltung/vertraege/neu?mietobjekt={wohnung}&beginn={Iso(beginn)}");
        Assert.Equal(wohnung.ToString(), neu.Dokument.QuerySelector("#mietobjekt option[selected]")!.GetAttribute("value"));
        var vertrag = await admin.AbsendenAsync(neu, "vertrag", new Dictionary<string, string>
        {
            ["Formular.Mieter1"] = anna.ToString(),
            ["Formular.Mieter2"] = ben.ToString(),
            ["Formular.Kaltmiete"] = "650",
            ["Formular.BetriebskostenVorauszahlung"] = "180",
            ["Formular.HeizkostenVorauszahlung"] = "90.50",
            ["Formular.Personen"] = "2",
            ["Formular.Kaution"] = "1950",
        });
        Assert.Contains("Der Vertrag wurde angelegt.", vertrag.Text, StringComparison.Ordinal);
        Assert.Contains("Anna Schmidt, Ben Weber", vertrag.Text, StringComparison.Ordinal);
        Assert.Contains("920,50 €", vertrag.Text, StringComparison.Ordinal);
        Assert.Contains("1.950,00 €", vertrag.Text, StringComparison.Ordinal);

        // Belegung in der Objektübersicht
        Assert.Contains("Anna Schmidt, Ben Weber", (await admin.LadenAsync($"/verwaltung/objekte/{objekt}")).Text, StringComparison.Ordinal);

        // Mieterhöhung ab Stichtag: alter Betrag bleibt im Verlauf
        var erhoehung = beginn.AddMonths(2);
        vertrag = await admin.AbsendenAsync(vertrag, "bestandteil", new Dictionary<string, string>
        {
            ["Bestandteil.Art"] = "Kaltmiete",
            ["Bestandteil.Ab"] = Iso(erhoehung),
            ["Bestandteil.Betrag"] = "700",
        });
        Assert.Contains("Der Betrag wurde gespeichert.", vertrag.Text, StringComparison.Ordinal);
        Assert.Contains("Verlauf", vertrag.Text, StringComparison.Ordinal);
        Assert.Contains("700,00 €", vertrag.Text, StringComparison.Ordinal);
        Assert.Contains($"{erhoehung.AddDays(-1):dd.MM.yyyy}", vertrag.Text, StringComparison.Ordinal);

        // Kündigung durch den Mieter: Termin nach § 573c
        var eingang = new DateOnly(Heute.Year, Heute.Month, 1).AddMonths(1).AddDays(14); // nach dem 3. Werktag
        vertrag = await admin.AbsendenAsync(vertrag, "kuendigung", new Dictionary<string, string> { ["Kuendigung.EingegangenAm"] = Iso(eingang), ["Kuendigung.Durch"] = "Mieter" });
        var erwartet = MietHahn.Domain.Vertraege.Mietvertrag.KuendigungsterminFuerMieter(eingang);
        Assert.Contains($"endet am {erwartet:dd.MM.yyyy}", vertrag.Text, StringComparison.Ordinal);

        vertrag = await admin.AbsendenAsync(vertrag, "kuendigung-aufheben");
        Assert.Contains("unbefristet", vertrag.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Zweiter_Vertrag_im_selben_Zeitraum_wird_abgelehnt()
    {
        var (admin, _) = await AngemeldetAlsAsync(Rollen.Admin);
        using var _ = admin;
        var (_, wohnung, anna, ben) = await StammdatenAsync();
        var felder = new Dictionary<string, string> { ["Formular.Mieter1"] = anna.ToString(), ["Formular.Kaltmiete"] = "650" };

        await admin.AbsendenAsync(await admin.LadenAsync($"/verwaltung/vertraege/neu?mietobjekt={wohnung}&beginn={Iso(Heute)}"), "vertrag", felder);
        var zweiter = await admin.AbsendenAsync(
            await admin.LadenAsync($"/verwaltung/vertraege/neu?mietobjekt={wohnung}&beginn={Iso(Heute.AddMonths(3))}"),
            "vertrag",
            new Dictionary<string, string>(felder) { ["Formular.Mieter1"] = ben.ToString() });

        Assert.Contains("Für dieses Mietobjekt gibt es in diesem Zeitraum bereits einen Vertrag.", zweiter.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ohne_Mieter_und_Kaltmiete_meldet_das_Formular_Fehler()
    {
        var (admin, _) = await AngemeldetAlsAsync(Rollen.Admin);
        using var _ = admin;
        var (_, wohnung, _, _) = await StammdatenAsync();

        var fehler = await admin.AbsendenAsync(await admin.LadenAsync($"/verwaltung/vertraege/neu?mietobjekt={wohnung}&beginn={Iso(Heute)}"), "vertrag");

        Assert.Contains("Bitte den Mieter wählen.", fehler.Text, StringComparison.Ordinal);
        Assert.Contains("Bitte die Kaltmiete angeben.", fehler.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Mieterwechsel_Auszug_neue_Anschrift_und_Einzug_vorbelegt()
    {
        var (admin, _) = await AngemeldetAlsAsync(Rollen.Admin);
        using var _ = admin;
        var (_, wohnung, anna, ben) = await StammdatenAsync();
        var vertrag = await admin.AbsendenAsync(
            await admin.LadenAsync($"/verwaltung/vertraege/neu?mietobjekt={wohnung}&beginn={Iso(Heute.AddMonths(-12))}"),
            "vertrag",
            new Dictionary<string, string> { ["Formular.Mieter1"] = anna.ToString(), ["Formular.Kaltmiete"] = "650" });
        var ende = Heute.AddDays(3);

        var auszug = await admin.AbsendenAsync(await admin.LadenAsync(vertrag.Pfad + "/auszug"), "auszug", new Dictionary<string, string>
        {
            ["Eingabe.Ende"] = Iso(ende),
            ["Eingabe.NeueStrasse"] = "Neue Straße 5",
            ["Eingabe.NeuePlz"] = "10115",
            ["Eingabe.NeuerOrt"] = "Berlin",
        });

        Assert.Contains($"endet am {ende:dd.MM.yyyy}", auszug.Text, StringComparison.Ordinal);
        var einzug = auszug.Dokument.QuerySelectorAll("a").Single(a => a.TextContent == "Neuen Vertrag anlegen").GetAttribute("href")!;
        Assert.Contains($"beginn={Iso(ende.AddDays(1))}", einzug, StringComparison.Ordinal);

        var neu = await admin.AbsendenAsync(await admin.LadenAsync(einzug), "vertrag", new Dictionary<string, string> { ["Formular.Mieter1"] = ben.ToString(), ["Formular.Kaltmiete"] = "690" });
        Assert.Contains("Der Vertrag wurde angelegt.", neu.Text, StringComparison.Ordinal);

        var historie = await admin.LadenAsync($"/verwaltung/einheiten/{wohnung}");
        Assert.Contains("Ben Weber", historie.Text, StringComparison.Ordinal);
        Assert.Contains("Anna Schmidt", historie.Text, StringComparison.Ordinal);

        App.Uhr.Advance(TimeSpan.FromDays(5)); // nach dem Auszug gilt die neue Anschrift
        var akte = await admin.LadenAsync($"/verwaltung/personen/{anna}");
        Assert.Contains("Neue Straße 5, 10115 Berlin", akte.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Buerge_hinzufuegen_und_entfernen()
    {
        var (admin, _) = await AngemeldetAlsAsync(Rollen.Admin);
        using var _ = admin;
        var (_, wohnung, anna, ben) = await StammdatenAsync();
        var vertrag = await admin.AbsendenAsync(
            await admin.LadenAsync($"/verwaltung/vertraege/neu?mietobjekt={wohnung}&beginn={Iso(Heute)}"),
            "vertrag",
            new Dictionary<string, string> { ["Formular.Mieter1"] = anna.ToString(), ["Formular.Kaltmiete"] = "650" });

        vertrag = await admin.AbsendenAsync(vertrag, "partei", new Dictionary<string, string> { ["NeuePartei.PersonId"] = ben.ToString(), ["NeuePartei.Rolle"] = "Buerge" });
        Assert.Contains("Die Person wurde hinzugefügt.", vertrag.Text, StringComparison.Ordinal);
        Assert.Contains("Bürge", vertrag.Text, StringComparison.Ordinal);

        vertrag = await admin.AbsendenAsync(vertrag, "partei-entfernen", new Dictionary<string, string> { ["EntfernenPersonId"] = ben.ToString() });
        Assert.Contains("Die Person wurde entfernt.", vertrag.Text, StringComparison.Ordinal);
        Assert.Empty(vertrag.Dokument.QuerySelectorAll("button[name=EntfernenPersonId]")); // einziger Mieter ist nicht entfernbar
    }

    [Fact]
    public async Task Ohne_Recht_Stammdaten_kein_Zugriff()
    {
        var (verwalter, _) = await AngemeldetAlsAsync(Rollen.Verwalter, [Recht.Finanzen]);
        using var _ = verwalter;

        Assert.Equal("/konto/zugriff-verweigert", (await verwalter.LadenAsync("/verwaltung/vertraege", weiterleitungenFolgen: false)).Weiterleitung!.AbsolutePath);
    }

    private async Task<(Guid Objekt, Guid Wohnung, Guid Anna, Guid Ben)> StammdatenAsync() =>
        await App.MitScopeAsync(async sp =>
        {
            var ct = TestContext.Current.CancellationToken;
            var objekte = sp.GetRequiredService<IObjektVerwaltung>();
            var objekt = (await objekte.AnlegenAsync(new ObjektDaten { Bezeichnung = "Lindenweg 3", Strasse = "Lindenweg 3", Plz = "50667", Ort = "Köln" }, ct)).Id!.Value;
            var wohnung = (await objekte.EinheitAnlegenAsync(objekt, new EinheitDaten { Bezeichnung = "Wohnung 1" }, ct)).Id!.Value;
            var personen = sp.GetRequiredService<IPersonenVerwaltung>();
            var anna = (await personen.AnlegenAsync(new PersonDaten { Vorname = "Anna", Nachname = "Schmidt" }, null, ct)).Id!.Value;
            var ben = (await personen.AnlegenAsync(new PersonDaten { Vorname = "Ben", Nachname = "Weber" }, null, ct)).Id!.Value;
            return (objekt, wohnung, anna, ben);
        });
}
