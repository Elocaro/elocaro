using MietHahn.Application.Konten;
using MietHahn.Web.Tests.Konto;

namespace MietHahn.Web.Tests.Bank;

public class BankimportTests(PostgresFixture postgres) : KontoTestBasis(postgres)
{
    [Fact]
    public async Task Konto_anlegen_Kontoauszug_mit_Vorschau_importieren_und_Umsatz_ignorieren()
    {
        var (admin, _) = await AngemeldetAlsAsync(Rollen.Admin);
        using var _ = admin;

        var konto = await admin.AbsendenAsync(await admin.LadenAsync("/verwaltung/bank/konten/neu"), "konto", new Dictionary<string, string>
        {
            ["Daten.Name"] = "Mietkonto",
            ["Daten.Iban"] = "DE89 3704 0044 0532 0130 00",
            ["Daten.Typ"] = "Mietkonto",
        });
        Assert.Contains("Das Konto wurde angelegt.", konto.Text, StringComparison.Ordinal);
        var kontoPfad = konto.Pfad;

        var vorschau = await admin.AbsendenMitDateiAsync(konto, "import", "Datei", Testdatei("camt053_v02.xml"), "februar.xml", "application/xml");
        Assert.Contains("Bitte die Vorschau prüfen", vorschau.Text, StringComparison.Ordinal);
        Assert.Contains("4 neue Umsätze", vorschau.Text, StringComparison.Ordinal);
        Assert.Contains("Nicht gebuchter Umsatz übersprungen", vorschau.Text, StringComparison.Ordinal);
        Assert.Contains("Miete Februar 2026 Wohnung 1 Lindenweg 3", vorschau.Text, StringComparison.Ordinal);

        var umsaetze = await admin.AbsendenAsync(vorschau, "importieren");
        Assert.Contains("4 Umsätze importiert.", umsaetze.Text, StringComparison.Ordinal);
        Assert.Contains("Stadtwerke Musterstadt", umsaetze.Text, StringComparison.Ordinal);
        Assert.Contains("4 Umsätze ohne Zuordnung", (await admin.LadenAsync("/verwaltung")).TextMitStreaming, StringComparison.Ordinal);

        // Zeilenknopf „Ignorieren“ mit Notiz; die Auswahl (Konto) bleibt erhalten.
        var knopf = umsaetze.Formular("status").QuerySelectorAll("button[name=Aktion]").First(b => b.GetAttribute("aria-label")!.Contains("05.02.2026", StringComparison.Ordinal));
        umsaetze = await admin.AbsendenAsync(umsaetze, "status", new Dictionary<string, string> { ["Aktion"] = knopf.GetAttribute("value")!, ["Notiz"] = "über Belege" });
        Assert.Contains("Der Umsatz wird ignoriert.", umsaetze.Text, StringComparison.Ordinal);
        Assert.Contains("Notiz: über Belege", umsaetze.Text, StringComparison.Ordinal);
        Assert.Contains("konto=", umsaetze.Url.Query, StringComparison.Ordinal);
        Assert.Equal(3, (await admin.LadenAsync(umsaetze.Url.PathAndQuery + "&status=Offen")).Dokument.QuerySelectorAll("tbody tr").Length);

        // Dieselbe Datei noch einmal: nichts Neues, Import gesperrt; verwerfen.
        vorschau = await admin.AbsendenMitDateiAsync(await admin.LadenAsync(kontoPfad), "import", "Datei", Testdatei("camt053_v02.xml"), "februar.xml", "application/xml");
        Assert.Contains("0 neue Umsätze, 4 schon vorhanden", vorschau.Text, StringComparison.Ordinal);
        Assert.True(vorschau.Formular("importieren").QuerySelector("button")!.HasAttribute("disabled"));
        konto = await admin.AbsendenAsync(vorschau, "verwerfen");
        Assert.Contains("Der Import wurde verworfen.", konto.Text, StringComparison.Ordinal);
        Assert.Contains("februar.xml", konto.Text, StringComparison.Ordinal); // Importprotokoll
    }

    [Fact]
    public async Task Unbekannte_Datei_wird_gemeldet_und_eigenes_CSV_Profil_angelegt()
    {
        var (admin, _) = await AngemeldetAlsAsync(Rollen.Admin);
        using var _ = admin;
        var konto = await admin.AbsendenAsync(await admin.LadenAsync("/verwaltung/bank/konten/neu"), "konto", new Dictionary<string, string>
        {
            ["Daten.Name"] = "Mietkonto",
            ["Daten.Iban"] = "DE89370400440532013000",
        });

        var csv = "Date,Payee,Memo,Amount\n2026-02-02,Anna Schmidt,Miete,770.00\n"u8.ToArray();
        var fehler = await admin.AbsendenMitDateiAsync(konto, "import", "Datei", csv, "export.csv", "text/csv");
        Assert.Contains("Das Dateiformat wurde nicht erkannt.", fehler.Text, StringComparison.Ordinal);

        var profile = await admin.AbsendenAsync(await admin.LadenAsync("/verwaltung/bank/csv-profile"), "profil", new Dictionary<string, string>
        {
            ["Daten.Name"] = "Hausbank",
            ["Daten.Trennzeichen"] = ",",
            ["Daten.Dezimaltrennzeichen"] = ".",
            ["Daten.Datumsformat"] = "yyyy-MM-dd",
            ["Daten.SpalteBuchungsdatum"] = "Date",
            ["Daten.SpalteBetrag"] = "Amount",
            ["Daten.SpalteName"] = "Payee",
            ["Daten.SpalteVerwendungszweck"] = "Memo",
        });
        Assert.Contains("Das Profil wurde angelegt.", profile.Text, StringComparison.Ordinal);

        var vorschau = await admin.AbsendenMitDateiAsync(await admin.LadenAsync(konto.Pfad), "import", "Datei", csv, "export.csv", "text/csv", new Dictionary<string, string> { ["Profil"] = "Hausbank" });
        Assert.Contains("1 neue Umsätze", vorschau.Text, StringComparison.Ordinal);
        Assert.Contains("CSV (Hausbank)", vorschau.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ohne_Recht_Finanzen_keine_Bankkonten()
    {
        var (verwalter, _) = await AngemeldetAlsAsync(Rollen.Verwalter, [Recht.Stammdaten]);
        using var _ = verwalter;

        Assert.Equal("/konto/zugriff-verweigert", (await verwalter.LadenAsync("/verwaltung/bank/konten", weiterleitungenFolgen: false)).Weiterleitung!.AbsolutePath);
        Assert.DoesNotContain("/verwaltung/bank", (await verwalter.LadenAsync("/verwaltung")).Html, StringComparison.Ordinal);
    }

    private static byte[] Testdatei(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Bank", "Testdateien", name));
}
