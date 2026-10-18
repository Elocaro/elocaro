using System.Net;

namespace MietHahn.Web.Tests.Konto;

public class ErsteinrichtungUndZweiFaktorTests(PostgresFixture postgres) : KontoTestBasis(postgres)
{
    [Fact]
    public async Task Ohne_Anmeldung_fuehrt_die_Startseite_zur_Anmeldung()
    {
        using var browser = App.Browser();

        var antwort = await browser.LadenAsync("/", weiterleitungenFolgen: false);

        Assert.Equal(HttpStatusCode.Redirect, antwort.Status);
        Assert.StartsWith("https://localhost/konto/anmelden?ReturnUrl=%2F", antwort.Weiterleitung!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Leere_Installation_fuehrt_zur_Ersteinrichtung()
    {
        using var browser = App.Browser();

        var seite = await browser.LadenAsync("/konto/anmelden");

        Assert.Equal("/konto/einrichtung", seite.Pfad);
        Assert.Contains("Ersteinrichtung", seite.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ersteinrichtung_erzwingt_Zwei_Faktor_vor_jeder_anderen_Seite()
    {
        using var browser = App.Browser();

        var nachEinrichtung = await EinrichtenAsync(browser);
        Assert.Equal("/konto/sicherheit/zwei-faktor", nachEinrichtung.Pfad);
        Assert.Contains("vorgeschrieben", nachEinrichtung.Text, StringComparison.Ordinal);

        // Andere Seiten sind gesperrt, bis 2FA aktiv ist.
        var start = await browser.LadenAsync("/", weiterleitungenFolgen: false);
        Assert.Equal("/konto/sicherheit/zwei-faktor", start.Weiterleitung!.ToString());

        var (_, codes, ergebnis) = await ZweiFaktorAktivierenAsync(browser);
        Assert.Contains("Die Zwei-Faktor-Anmeldung ist jetzt aktiv.", ergebnis.Text, StringComparison.Ordinal);
        Assert.Equal(10, codes.Count);

        var danach = await browser.LadenAsync("/");
        Assert.Equal(HttpStatusCode.OK, danach.Status);
        Assert.Equal("/", danach.Pfad);
    }

    [Fact]
    public async Task Falscher_Code_aktiviert_Zwei_Faktor_nicht()
    {
        using var browser = App.Browser();
        await EinrichtenAsync(browser);
        var seite = await browser.LadenAsync("/konto/sicherheit/zwei-faktor");

        var ergebnis = await browser.AbsendenAsync(seite, "zwei-faktor-einrichten", new Dictionary<string, string> { ["Eingabe.Code"] = "000000" });

        Assert.Contains("Der Code ist ungültig", ergebnis.Text, StringComparison.Ordinal);
        Assert.Equal("/konto/sicherheit/zwei-faktor", (await browser.LadenAsync("/", weiterleitungenFolgen: false)).Weiterleitung!.ToString());
    }

    [Fact]
    public async Task Ersteinrichtung_ist_nur_einmal_moeglich()
    {
        using (var erster = App.Browser())
        {
            await EinrichtenAsync(erster);
        }

        using var zweiter = App.Browser();
        var seite = await zweiter.LadenAsync("/konto/einrichtung");
        Assert.Equal("/konto/anmelden", seite.Pfad);
    }

    [Fact]
    public async Task Zu_kurzes_Passwort_wird_abgelehnt()
    {
        using var browser = App.Browser();

        var ergebnis = await EinrichtenAsync(browser, AdminEmail, "kurz");

        Assert.Equal("/konto/einrichtung", ergebnis.Pfad);
        Assert.Contains("mindestens 12 Zeichen", ergebnis.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Anmeldung_mit_Passwort_und_Zwei_Faktor_Code()
    {
        string schluessel;
        using (var einrichtung = App.Browser())
        {
            await EinrichtenAsync(einrichtung);
            (schluessel, _, _) = await ZweiFaktorAktivierenAsync(einrichtung);
        }

        using var browser = App.Browser();
        var zweiFaktor = await AnmeldenAsync(browser, AdminEmail, AdminPasswort, returnUrl: "/verwaltung/system/jobs");
        Assert.Equal("/konto/anmelden/zwei-faktor", zweiFaktor.Pfad);

        var falsch = await browser.AbsendenAsync(zweiFaktor, "zwei-faktor", new Dictionary<string, string> { ["Eingabe.Code"] = "123456" });
        Assert.Contains("Der Code ist ungültig.", falsch.Text, StringComparison.Ordinal);

        var richtig = await browser.AbsendenAsync(falsch, "zwei-faktor", new Dictionary<string, string> { ["Eingabe.Code"] = Code(schluessel) });
        Assert.Equal("/verwaltung/system/jobs", richtig.Pfad);
        Assert.Equal(HttpStatusCode.OK, richtig.Status);
    }

    [Fact]
    public async Task Wiederherstellungscode_funktioniert_genau_einmal()
    {
        IReadOnlyList<string> codes;
        using (var einrichtung = App.Browser())
        {
            await EinrichtenAsync(einrichtung);
            (_, codes, _) = await ZweiFaktorAktivierenAsync(einrichtung);
        }

        for (var versuch = 0; versuch < 2; versuch++)
        {
            using var browser = App.Browser();
            await AnmeldenAsync(browser, AdminEmail, AdminPasswort);
            var seite = await browser.LadenAsync("/konto/anmelden/wiederherstellungscode");
            var ergebnis = await browser.AbsendenAsync(seite, "wiederherstellung", new Dictionary<string, string> { ["Eingabe.Code"] = codes[0] });

            if (versuch == 0)
            {
                Assert.Equal("/", ergebnis.Pfad);
            }
            else
            {
                Assert.Contains("Der Wiederherstellungscode ist ungültig.", ergebnis.Text, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public async Task Abmelden_beendet_die_Sitzung()
    {
        using var browser = App.Browser();
        await EinrichtenAsync(browser);
        await ZweiFaktorAktivierenAsync(browser);

        await AbmeldenAsync(browser);

        var start = await browser.LadenAsync("/", weiterleitungenFolgen: false);
        Assert.Equal(HttpStatusCode.Redirect, start.Status);
    }

    [Fact]
    public async Task Abmelden_ohne_Antiforgery_Token_wird_abgelehnt()
    {
        using var browser = App.Browser();
        await EinrichtenAsync(browser);
        await ZweiFaktorAktivierenAsync(browser);

        using var inhalt = new FormUrlEncodedContent([]);
        using var antwort = await browser.Http.PostAsync(new Uri("/konto/abmelden", UriKind.Relative), inhalt, TestContext.Current.CancellationToken);

        Assert.NotEqual(HttpStatusCode.Redirect, antwort.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await browser.LadenAsync("/")).Status);
    }
}
