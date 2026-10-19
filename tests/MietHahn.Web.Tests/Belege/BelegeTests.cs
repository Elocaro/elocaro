using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Konten;
using MietHahn.Application.Objekte;
using MietHahn.Web.Tests.Konto;

namespace MietHahn.Web.Tests.Belege;

public class BelegeTests(PostgresFixture postgres) : KontoTestBasis(postgres)
{
    private static readonly byte[] Pdf = "%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\ntrailer << /Root 1 0 R >>\n%%EOF\n"u8.ToArray();

    [Fact]
    public async Task Rechnung_hochladen_und_in_zwei_Kostenarten_aufteilen()
    {
        var (admin, _) = await AngemeldetAlsAsync(Rollen.Admin);
        using var _ = admin;
        var haus = await ObjektAsync();

        var beleg = await admin.AbsendenMitDateiAsync(await admin.LadenAsync("/verwaltung/belege/neu"), "beleg", "Dateien", Pdf, "rechnung.pdf", "application/pdf", new Dictionary<string, string>
        {
            ["Daten.LieferantName"] = "Grünpflege Meier",
            ["Daten.Rechnungsnummer"] = "R-117",
            ["Daten.Rechnungsdatum"] = "2026-04-30",
            ["Daten.Brutto"] = "595.00",
            ["Daten.Netto"] = "500",
            ["Daten.Umsatzsteuer"] = "95",
        });
        Assert.Contains("Der Beleg wurde gespeichert.", beleg.Text, StringComparison.Ordinal);
        Assert.Contains("Noch 595,00 € nicht aufgeteilt", beleg.Text, StringComparison.Ordinal);
        Assert.NotNull(beleg.Dokument.QuerySelector("iframe.dokument-vorschau"));

        beleg = await admin.AbsendenAsync(beleg, "position", new Dictionary<string, string>
        {
            ["Position.Kostenart"] = "betrkv-14",
            ["Position.ObjektId"] = haus.ToString(),
            ["Position.Betrag"] = "357",
            ["Position.Paragraf35a"] = "HaushaltsnaheDienstleistung",
            ["Position.Arbeitskosten"] = "357",
        });
        Assert.Contains("Die Position wurde hinzugefügt.", beleg.Text, StringComparison.Ordinal);
        Assert.Contains("Noch 238,00 € nicht aufgeteilt", beleg.Text, StringComparison.Ordinal);

        var zuViel = await admin.AbsendenAsync(beleg, "position", new Dictionary<string, string>
        {
            ["Position.Kostenart"] = "betrkv-10",
            ["Position.ObjektId"] = haus.ToString(),
            ["Position.Betrag"] = "300",
        });
        Assert.Contains("übersteigen den Rechnungsbetrag", zuViel.Text, StringComparison.Ordinal);

        beleg = await admin.AbsendenAsync(beleg, "position", new Dictionary<string, string>
        {
            ["Position.Kostenart"] = "betrkv-10",
            ["Position.ObjektId"] = haus.ToString(),
        });
        Assert.DoesNotContain("nicht aufgeteilt", beleg.Text, StringComparison.Ordinal);
        Assert.Contains("Hauswart", beleg.Text, StringComparison.Ordinal);
        Assert.Contains("Gartenpflege", beleg.Text, StringComparison.Ordinal);
        Assert.Contains("595,00 €", beleg.Dokument.QuerySelector("tfoot")!.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Position hinzufügen", beleg.Text, StringComparison.Ordinal); // vollständig aufgeteilt

        var liste = await admin.LadenAsync("/verwaltung/belege?kostenart=betrkv-10&jahr=2026");
        Assert.Contains("Grünpflege Meier", liste.Text, StringComparison.Ordinal);
        Assert.Contains("unbezahlt", liste.Text, StringComparison.Ordinal);
        Assert.Contains("Keine Belege in dieser Auswahl.", (await admin.LadenAsync("/verwaltung/belege?aufteilen=true")).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Beleg_mit_einer_Kostenart_direkt_erfassen_und_loeschen()
    {
        var (admin, _) = await AngemeldetAlsAsync(Rollen.Admin);
        using var _ = admin;
        var haus = await ObjektAsync();

        var beleg = await admin.AbsendenAsync(await admin.LadenAsync("/verwaltung/belege/neu"), "beleg", new Dictionary<string, string>
        {
            ["Daten.LieferantName"] = "Stadt Köln",
            ["Daten.Rechnungsdatum"] = "2026-02-15",
            ["Daten.Brutto"] = "480",
            ["Daten.BezahltAm"] = "2026-02-15",
            ["Position.Kostenart"] = "betrkv-01",
            ["Position.ObjektId"] = haus.ToString(),
        });
        Assert.Contains("Laufende öffentliche Lasten (Grundsteuer)", beleg.Text, StringComparison.Ordinal);
        Assert.Contains("01.01.2026 – 31.12.2026", beleg.Text, StringComparison.Ordinal);
        Assert.Contains("Noch keine Datei.", beleg.Text, StringComparison.Ordinal);

        var liste = await admin.AbsendenAsync(beleg, "loeschen");
        Assert.Contains("Der Beleg wurde gelöscht.", liste.Text, StringComparison.Ordinal);
        Assert.Contains("Keine Belege in dieser Auswahl.", liste.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pflichtangaben_und_Recht_Finanzen()
    {
        var (admin, _) = await AngemeldetAlsAsync(Rollen.Admin);
        using var _ = admin;
        var fehler = await admin.AbsendenAsync(await admin.LadenAsync("/verwaltung/belege/neu"), "beleg", new Dictionary<string, string> { ["Daten.Brutto"] = "10" });
        Assert.Contains("Bitte das Rechnungsdatum angeben.", fehler.Text, StringComparison.Ordinal);

        var (verwalter, _) = await AngemeldetAlsAsync(Rollen.Verwalter, [Recht.Stammdaten]);
        using var __ = verwalter;
        Assert.Equal("/konto/zugriff-verweigert", (await verwalter.LadenAsync("/verwaltung/belege", weiterleitungenFolgen: false)).Weiterleitung!.AbsolutePath);
    }

    private Task<Guid> ObjektAsync() =>
        App.MitScopeAsync(async sp => (await sp.GetRequiredService<IObjektVerwaltung>().AnlegenAsync(
            new ObjektDaten { Bezeichnung = "Lindenweg 3", Strasse = "Lindenweg 3", Plz = "50667", Ort = "Köln" }, TestContext.Current.CancellationToken)).Id!.Value);
}
