using System.Net;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Konten;
using MietHahn.Application.Verwaltung;
using MietHahn.Web.Tests.Konto;

namespace MietHahn.Web.Tests.Einstellungen;

public class VermieterTests(PostgresFixture postgres) : KontoTestBasis(postgres)
{
    private static readonly Dictionary<string, string> GueltigeAngaben = new()
    {
        ["Daten.Name"] = "Andy Hahn",
        ["Daten.Strasse"] = "Musterweg 1",
        ["Daten.Plz"] = "12345",
        ["Daten.Ort"] = "Musterstadt",
        ["Daten.Iban"] = "de02120300000000202051",
        ["Daten.Bic"] = "byladem1001",
    };

    [Fact]
    public async Task Admin_legt_das_Vermieterprofil_an_und_aendert_es()
    {
        var (admin, _) = await AngemeldetAlsAsync(Rollen.Admin);
        using var _ = admin;

        var leer = await admin.LadenAsync("/verwaltung/einstellungen/vermieter");
        Assert.Contains("Noch kein Vermieterprofil hinterlegt", leer.Text, StringComparison.Ordinal);

        var gespeichert = await admin.AbsendenAsync(leer, "vermieter", GueltigeAngaben);
        Assert.Contains("Die Vermieterangaben wurden gespeichert.", gespeichert.Text, StringComparison.Ordinal);
        Assert.Equal("DE02 1203 0000 0000 2020 51", gespeichert.Dokument.QuerySelector("#iban")!.GetAttribute("value"));
        Assert.Equal("BYLADEM1001", gespeichert.Dokument.QuerySelector("#bic")!.GetAttribute("value"));

        var geaendert = await admin.AbsendenAsync(gespeichert, "vermieter", new Dictionary<string, string>(GueltigeAngaben) { ["Daten.Ort"] = "Neustadt" });
        Assert.Equal("Neustadt", geaendert.Dokument.QuerySelector("#ort")!.GetAttribute("value"));

        var profil = await App.MitScopeAsync(sp => sp.GetRequiredService<IVermieterprofilVerwaltung>().LadenAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Neustadt", profil!.Ort);
    }

    [Fact]
    public async Task Ungueltige_Postleitzahl_wird_abgelehnt()
    {
        var (admin, _) = await AngemeldetAlsAsync(Rollen.Admin);
        using var _ = admin;

        var ergebnis = await admin.AbsendenAsync(await admin.LadenAsync("/verwaltung/einstellungen/vermieter"), "vermieter",
            new Dictionary<string, string>(GueltigeAngaben) { ["Daten.Plz"] = "1234" });

        Assert.Contains("Die Postleitzahl hat 5 Ziffern.", ergebnis.Text, StringComparison.Ordinal);
        Assert.Null(await App.MitScopeAsync(sp => sp.GetRequiredService<IVermieterprofilVerwaltung>().LadenAsync(TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task Ungueltige_E_Mail_wird_abgelehnt_leere_ist_erlaubt()
    {
        var (admin, _) = await AngemeldetAlsAsync(Rollen.Admin);
        using var _ = admin;
        var seite = await admin.LadenAsync("/verwaltung/einstellungen/vermieter");

        var ungueltig = await admin.AbsendenAsync(seite, "vermieter", new Dictionary<string, string>(GueltigeAngaben) { ["Daten.Email"] = "kein-at" });
        var leer = await admin.AbsendenAsync(seite, "vermieter", new Dictionary<string, string>(GueltigeAngaben) { ["Daten.Email"] = "" });

        Assert.Contains("Bitte eine gültige E-Mail-Adresse angeben.", ungueltig.Text, StringComparison.Ordinal);
        Assert.Contains("Die Vermieterangaben wurden gespeichert.", leer.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Verwalter_ohne_Systemrecht_hat_keinen_Zugriff()
    {
        var (verwalter, _) = await AngemeldetAlsAsync(Rollen.Verwalter);
        using var _ = verwalter;

        var antwort = await verwalter.LadenAsync("/verwaltung/einstellungen/vermieter", weiterleitungenFolgen: false);

        Assert.Equal(HttpStatusCode.Redirect, antwort.Status);
        Assert.Equal("/konto/zugriff-verweigert", antwort.Weiterleitung!.AbsolutePath);
    }
}
