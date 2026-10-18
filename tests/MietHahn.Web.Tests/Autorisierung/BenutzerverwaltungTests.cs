using System.Net;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Audit;
using MietHahn.Application.Konten;
using MietHahn.Web.Tests.Konto;

namespace MietHahn.Web.Tests.Autorisierung;

public class BenutzerverwaltungTests(PostgresFixture postgres) : KontoTestBasis(postgres)
{
    [Fact]
    public async Task Admin_laedt_ueber_die_Oberflaeche_ein()
    {
        var (admin, _) = await AngemeldetAlsAsync(Rollen.Admin);
        using var _ = admin;

        var liste = await admin.LadenAsync("/verwaltung/benutzer");
        var ergebnis = await admin.AbsendenAsync(liste, "einladen", new Dictionary<string, string>
        {
            ["Einladung.Anzeigename"] = "Erika Mustermann",
            ["Einladung.Email"] = "erika@example.de",
            ["Einladung.Rolle"] = Rollen.Mieter,
        });

        Assert.Contains("Die Einladung an erika@example.de wurde verschickt.", ergebnis.Text, StringComparison.Ordinal);
        var zeile = ergebnis.Dokument.QuerySelectorAll("tbody tr").Single(z => z.TextContent.Contains("erika@example.de", StringComparison.Ordinal));
        Assert.Contains("Mieter", zeile.TextContent, StringComparison.Ordinal);
        Assert.Contains("eingeladen", zeile.TextContent, StringComparison.Ordinal);
        Assert.NotNull(await LetzteMailAnAsync("erika@example.de"));
    }

    [Fact]
    public async Task Gesperrtes_Konto_wird_sofort_abgemeldet()
    {
        var (admin, _) = await AngemeldetAlsAsync(Rollen.Admin);
        var (mieter, mieterId) = await AngemeldetAlsAsync(Rollen.Mieter);
        using var a = admin;
        using var m = mieter;
        Assert.Equal(HttpStatusCode.OK, (await mieter.LadenAsync("/portal")).Status);

        var details = await admin.LadenAsync($"/verwaltung/benutzer/{mieterId}");
        var gesperrt = await admin.AbsendenAsync(details, "sperren");
        Assert.Contains("Das Konto wurde gesperrt.", gesperrt.Text, StringComparison.Ordinal);

        var nachher = await mieter.LadenAsync("/portal", weiterleitungenFolgen: false);
        Assert.Equal("/konto/anmelden", nachher.Weiterleitung!.AbsolutePath);

        var entsperrt = await admin.AbsendenAsync(await admin.LadenAsync($"/verwaltung/benutzer/{mieterId}"), "sperren");
        Assert.Contains("Das Konto wurde entsperrt.", entsperrt.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rolle_und_Rechte_aendern_wirkt_sofort_und_wird_protokolliert()
    {
        var (admin, _) = await AngemeldetAlsAsync(Rollen.Admin);
        var (verwalter, verwalterId) = await AngemeldetAlsAsync(Rollen.Verwalter);
        using var a = admin;
        using var v = verwalter;
        Assert.Equal(HttpStatusCode.Redirect, (await verwalter.LadenAsync("/verwaltung/benutzer", weiterleitungenFolgen: false)).Status);

        var details = await admin.LadenAsync($"/verwaltung/benutzer/{verwalterId}");
        var formular = details.Formular("rechte");
        using var inhalt = new FormUrlEncodedContent(
        [
            new("_handler", "rechte"),
            new("__RequestVerificationToken", formular.QuerySelector("input[name=__RequestVerificationToken]")!.GetAttribute("value")!),
            new("RechteEingabe.Auswahl", nameof(Recht.Stammdaten)),
            new("RechteEingabe.Auswahl", nameof(Recht.Benutzerverwaltung)),
        ]);
        using var antwort = await admin.Http.PostAsync(details.Url, inhalt, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, antwort.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await verwalter.LadenAsync("/verwaltung/benutzer", weiterleitungenFolgen: false)).Status);

        var historie = await App.MitScopeAsync(sp => sp.GetRequiredService<IAenderungsHistorie>().LadenAsync("Benutzer", verwalterId, TestContext.Current.CancellationToken));
        Assert.Contains(historie.SelectMany(h => h.Aenderungen), f => f is { Feld: "Rechte", Alt: "Stammdaten, Finanzen", Neu: "Stammdaten, Benutzerverwaltung" });

        // Rollenwechsel zum Mieter: kein Zugriff mehr auf die Verwaltung.
        var nachRolle = await admin.AbsendenAsync(await admin.LadenAsync($"/verwaltung/benutzer/{verwalterId}"), "rolle", new Dictionary<string, string> { ["RolleEingabe.Rolle"] = Rollen.Mieter });
        Assert.Contains("Die Rolle wurde geändert.", nachRolle.Text, StringComparison.Ordinal);
        Assert.Equal("/konto/zugriff-verweigert", (await verwalter.LadenAsync("/verwaltung", weiterleitungenFolgen: false)).Weiterleitung!.AbsolutePath);
    }

    [Fact]
    public async Task Letzter_Admin_kann_weder_gesperrt_noch_herabgestuft_werden()
    {
        var (admin, adminId) = await AngemeldetAlsAsync(Rollen.Admin);
        using var _ = admin;

        var ergebnis = await admin.AbsendenAsync(await admin.LadenAsync($"/verwaltung/benutzer/{adminId}"), "rolle", new Dictionary<string, string> { ["RolleEingabe.Rolle"] = Rollen.Verwalter });

        Assert.Contains("Der letzte aktive Administrator kann seine Rolle nicht abgeben.", ergebnis.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Konto sperren", ergebnis.Text, StringComparison.Ordinal); // eigenes Konto: keine Sperre anbieten

        var sperre = await App.MitScopeAsync(sp => sp.GetRequiredService<IBenutzerverwaltung>().SperrenAsync(adminId, TestContext.Current.CancellationToken));
        Assert.False(sperre.Erfolgreich);
    }

    [Fact]
    public async Task Zweiter_Admin_darf_den_ersten_sperren()
    {
        var (_, ersterId) = await AngemeldetAlsAsync(Rollen.Admin);
        var (zweiter, _) = await AngemeldetAlsAsync(Rollen.Admin);
        using var _ = zweiter;

        var ergebnis = await zweiter.AbsendenAsync(await zweiter.LadenAsync($"/verwaltung/benutzer/{ersterId}"), "sperren");

        Assert.Contains("Das Konto wurde gesperrt.", ergebnis.Text, StringComparison.Ordinal);
    }
}
