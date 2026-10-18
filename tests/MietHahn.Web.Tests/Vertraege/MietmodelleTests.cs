using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Konten;
using MietHahn.Application.Objekte;
using MietHahn.Application.Personen;
using MietHahn.Application.Vertraege;
using MietHahn.Domain.Gemeinsam;
using MietHahn.Web.Tests.Konto;

namespace MietHahn.Web.Tests.Vertraege;

public class MietmodelleTests(PostgresFixture postgres) : KontoTestBasis(postgres)
{
    private DateOnly Heute => Zeitzone.Heute(App.Uhr.GetUtcNow());

    [Fact]
    public async Task Staffelmiete_erfassen_und_Staffel_entfernen()
    {
        var (admin, _) = await AngemeldetAlsAsync(Rollen.Admin);
        using var _ = admin;
        var vertrag = await admin.LadenAsync($"/verwaltung/vertraege/{await VertragAsync(Heute)}");

        vertrag = await admin.AbsendenAsync(vertrag, "mietmodell", new Dictionary<string, string> { ["Eingabe.Modell"] = "Staffel" });
        Assert.Contains("Mietmodell: Staffelmiete", vertrag.Text, StringComparison.Ordinal);

        var ab = Heute.AddYears(1);
        vertrag = await admin.AbsendenAsync(vertrag, "staffel", new Dictionary<string, string> { ["Staffel.Ab"] = Iso(ab), ["Staffel.Kaltmiete"] = "700" });
        Assert.Contains("Die Staffel wurde gespeichert.", vertrag.Text, StringComparison.Ordinal);
        Assert.Contains("geplant", vertrag.Text, StringComparison.Ordinal);

        var zuFrueh = await admin.AbsendenAsync(vertrag, "staffel", new Dictionary<string, string> { ["Staffel.Ab"] = Iso(ab.AddMonths(6)), ["Staffel.Kaltmiete"] = "720" });
        Assert.Contains("mindestens ein Jahr unverändert", zuFrueh.Text, StringComparison.Ordinal);

        var staffelId = vertrag.Dokument.QuerySelector("button[name=EntfernenStaffelId]")!.GetAttribute("value")!;
        vertrag = await admin.AbsendenAsync(vertrag, "staffel-entfernen", new Dictionary<string, string> { ["EntfernenStaffelId"] = staffelId });
        Assert.Contains("Die Staffel wurde entfernt.", vertrag.Text, StringComparison.Ordinal);
        Assert.Contains("Noch keine Staffeln erfasst.", vertrag.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Indexmiete_mit_VPI_aus_dem_Import_anpassen()
    {
        var (admin, _) = await AngemeldetAlsAsync(Rollen.Admin);
        using var _ = admin;
        var beginn = Heute.AddMonths(-14);
        var vertragsseite = $"/verwaltung/vertraege/{await VertragAsync(beginn)}";
        var basisMonat = new DateOnly(beginn.Year, beginn.Month, 1);

        var vertrag = await admin.AbsendenAsync(await admin.LadenAsync(vertragsseite), "mietmodell", new Dictionary<string, string>
        {
            ["Eingabe.Modell"] = "Index",
            ["Eingabe.BasisMonat"] = basisMonat.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            ["Eingabe.BasisWert"] = "117.4",
        });
        Assert.Contains("Mietmodell: Indexmiete", vertrag.Text, StringComparison.Ordinal);
        Assert.Contains("Kein VPI-Wert neuer als der Ausgangsindex.", vertrag.Text, StringComparison.Ordinal);

        var neuerMonat = new DateOnly(Heute.Year, Heute.Month, 1).AddMonths(-2);
        var vpi = await admin.AbsendenAsync(await admin.LadenAsync("/verwaltung/vpi"), "vpi-import", new Dictionary<string, string>
        {
            ["Import.Csv"] = $"Jahr;Monat;Index\n{basisMonat:yyyy-MM};117,4\n{neuerMonat:yyyy-MM};121,7\n",
        });
        Assert.Contains("Import: 2 neu, 0 geändert, 0 unverändert.", vpi.Text, StringComparison.Ordinal);

        vertrag = await admin.LadenAsync(vertragsseite);
        Assert.Contains("neue Kaltmiete 673,81 €", vertrag.Text, StringComparison.Ordinal);
        Assert.Contains("+3,7 %", vertrag.Text, StringComparison.Ordinal);

        vertrag = await admin.AbsendenAsync(vertrag, "index-anpassung", new Dictionary<string, string> { ["Anpassung.ErklaerungAm"] = Iso(Heute) });
        Assert.Contains("Die Indexanpassung wurde übernommen.", vertrag.Text, StringComparison.Ordinal);
        Assert.Contains("673,81 €", vertrag.Text, StringComparison.Ordinal); // im Verlauf ab dem übernächsten Monat
        Assert.Contains("121,7", vertrag.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Vpi_Wert_manuell_erfassen()
    {
        var (admin, _) = await AngemeldetAlsAsync(Rollen.Admin);
        using var _ = admin;

        var seite = await admin.AbsendenAsync(await admin.LadenAsync("/verwaltung/vpi"), "vpi", new Dictionary<string, string> { ["Eingabe.Monat"] = "2026-09", ["Eingabe.Wert"] = "122.1" });

        Assert.Contains("Der Indexwert wurde gespeichert.", seite.Text, StringComparison.Ordinal);
        Assert.Contains("September 2026", seite.Text, StringComparison.Ordinal);
        Assert.Contains("122,1", seite.Text, StringComparison.Ordinal);
    }

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
