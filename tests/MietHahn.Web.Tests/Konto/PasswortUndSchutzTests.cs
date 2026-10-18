using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MietHahn.Infrastructure.Identitaet;
using MietHahn.Web.Konto;

namespace MietHahn.Web.Tests.Konto;

public class PasswortUndSchutzTests(PostgresFixture postgres) : KontoTestBasis(postgres)
{
    [Fact]
    public async Task Falsches_Passwort_gibt_eine_neutrale_Meldung()
    {
        using (var einrichtung = App.Browser())
        {
            await EinrichtenAsync(einrichtung);
        }

        using var browser = App.Browser();
        var falsch = await AnmeldenAsync(browser, AdminEmail, "Falsches-Passwort-123");
        var unbekannt = await AnmeldenAsync(browser, "niemand@example.de", "Falsches-Passwort-123");

        Assert.Contains("E-Mail-Adresse oder Passwort ist falsch.", falsch.Text, StringComparison.Ordinal);
        Assert.Contains("E-Mail-Adresse oder Passwort ist falsch.", unbekannt.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Nach_fuenf_Fehlversuchen_ist_das_Konto_voruebergehend_gesperrt()
    {
        using (var einrichtung = App.Browser())
        {
            await EinrichtenAsync(einrichtung);
        }

        using var browser = App.Browser();
        Seite seite = null!;
        for (var i = 0; i < 5; i++)
        {
            seite = await AnmeldenAsync(browser, AdminEmail, "Falsches-Passwort-123");
        }

        Assert.Equal("/konto/gesperrt", seite.Pfad);
        Assert.Equal("/konto/gesperrt", (await AnmeldenAsync(browser, AdminEmail, AdminPasswort)).Pfad);

        // Identity rechnet mit der Systemuhr: Sperrdauer prüfen und das Ablaufen simulieren.
        await App.MitScopeAsync(async sp =>
        {
            var manager = sp.GetRequiredService<UserManager<Benutzer>>();
            var benutzer = (await manager.FindByEmailAsync(AdminEmail))!;
            var ende = await manager.GetLockoutEndDateAsync(benutzer);
            Assert.InRange(ende!.Value - DateTimeOffset.UtcNow, TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(15));
            return await manager.SetLockoutEndDateAsync(benutzer, DateTimeOffset.UtcNow.AddSeconds(-1));
        });

        Assert.Equal("/konto/sicherheit/zwei-faktor", (await AnmeldenAsync(browser, AdminEmail, AdminPasswort)).Pfad);
    }

    [Fact]
    public async Task Passwort_vergessen_und_zuruecksetzen()
    {
        using (var einrichtung = App.Browser())
        {
            await EinrichtenAsync(einrichtung);
        }

        using var browser = App.Browser();
        var anfordern = await browser.LadenAsync("/konto/passwort-vergessen");
        var bestaetigung = await browser.AbsendenAsync(anfordern, "passwort-vergessen", new Dictionary<string, string> { ["Eingabe.Email"] = AdminEmail });
        Assert.Contains("Falls ein passendes Konto existiert", bestaetigung.Text, StringComparison.Ordinal);

        var mail = (await LetzteMailAnAsync(AdminEmail))!;
        Assert.Equal("Passwort zurücksetzen", mail.Betreff);
        var formular = await browser.LadenAsync(LinkAus(mail));
        var ergebnis = await browser.AbsendenAsync(formular, "passwort-zuruecksetzen", new Dictionary<string, string>
        {
            ["Eingabe.Passwort"] = "Neues-Passwort-2026!",
            ["Eingabe.PasswortWiederholung"] = "Neues-Passwort-2026!",
        });

        Assert.Equal("/konto/anmelden", ergebnis.Pfad);
        Assert.Contains("Ihr Passwort wurde geändert", ergebnis.Text, StringComparison.Ordinal);
        Assert.Contains("E-Mail-Adresse oder Passwort ist falsch.", (await AnmeldenAsync(browser, AdminEmail, AdminPasswort)).Text, StringComparison.Ordinal);
        // Admin ohne eingerichtete 2FA landet nach der Anmeldung direkt bei der Pflicht-Einrichtung.
        Assert.Equal("/konto/sicherheit/zwei-faktor", (await AnmeldenAsync(browser, AdminEmail, "Neues-Passwort-2026!")).Pfad);
    }

    [Fact]
    public async Task Link_zum_Zuruecksetzen_ist_nur_einmal_verwendbar()
    {
        using var browser = App.Browser();
        await EinrichtenAsync(browser);
        await browser.AbsendenAsync(await browser.LadenAsync("/konto/passwort-vergessen"), "passwort-vergessen", new Dictionary<string, string> { ["Eingabe.Email"] = AdminEmail });
        var link = LinkAus((await LetzteMailAnAsync(AdminEmail))!);
        var felder = new Dictionary<string, string>
        {
            ["Eingabe.Passwort"] = "Neues-Passwort-2026!",
            ["Eingabe.PasswortWiederholung"] = "Neues-Passwort-2026!",
        };

        await browser.AbsendenAsync(await browser.LadenAsync(link), "passwort-zuruecksetzen", felder);
        var zweitesMal = await browser.AbsendenAsync(await browser.LadenAsync(link), "passwort-zuruecksetzen", felder);

        Assert.Contains("Der Link ist ungültig oder abgelaufen.", zweitesMal.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Token_Laufzeiten_sind_2_Stunden_fuer_Reset_und_7_Tage_fuer_Einladungen()
    {
        // Identity prüft die Laufzeit selbst (mit der Systemuhr); hier wird die Konfiguration abgesichert.
        Assert.Equal(TimeSpan.FromHours(2), App.Services.GetRequiredService<IOptions<DataProtectionTokenProviderOptions>>().Value.TokenLifespan);
        Assert.Equal(TimeSpan.FromDays(7), App.Services.GetRequiredService<IOptions<EinladungsTokenOptionen>>().Value.TokenLifespan);
    }

    [Fact]
    public async Task Gesperrtes_Konto_kann_sich_nicht_anmelden()
    {
        using (var einrichtung = App.Browser())
        {
            await EinrichtenAsync(einrichtung);
        }

        await App.MitScopeAsync(async sp =>
        {
            var manager = sp.GetRequiredService<UserManager<Benutzer>>();
            var benutzer = await manager.FindByEmailAsync(AdminEmail);
            benutzer!.Gesperrt = true;
            return await manager.UpdateAsync(benutzer);
        });

        using var browser = App.Browser();
        Assert.Contains("E-Mail-Adresse oder Passwort ist falsch.", (await AnmeldenAsync(browser, AdminEmail, AdminPasswort)).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Externe_Ruecksprungadresse_wird_ignoriert()
    {
        using var browser = App.Browser();
        await EinrichtenAsync(browser);
        var (schluessel, _, _) = await ZweiFaktorAktivierenAsync(browser);
        await AbmeldenAsync(browser);

        var zweiFaktor = await AnmeldenAsync(browser, AdminEmail, AdminPasswort, returnUrl: "https://boese.example/phishing");
        var ergebnis = await browser.AbsendenAsync(zweiFaktor, "zwei-faktor", new Dictionary<string, string> { ["Eingabe.Code"] = Code(schluessel) }, weiterleitungenFolgen: false);

        Assert.Equal("/", ergebnis.Weiterleitung?.ToString().Replace("https://localhost", "", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("/verwaltung", "/verwaltung")]
    [InlineData("verwaltung?x=1", "/verwaltung?x=1")]
    [InlineData("https://boese.example", "/")]
    [InlineData("//boese.example", "/")]
    [InlineData("/\\boese.example", "/")]
    public void Nur_lokale_Weiterleitungsziele_sind_erlaubt(string? ziel, string erwartet)
    {
        Assert.Equal(erwartet, Weiterleitung.LokalesZiel(ziel));
    }
}

public class RateLimitTests(PostgresFixture postgres) : KontoTestBasis(postgres)
{
    protected override IDictionary<string, string?> Konfiguration => new Dictionary<string, string?> { ["Anmeldung:AnfragenProMinute"] = "3" };

    [Fact]
    public async Task Zu_viele_Anmeldeversuche_werden_gedrosselt()
    {
        using var browser = App.Browser();
        var seite = await browser.LadenAsync("/konto/link");

        var status = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            status.Add((await browser.AbsendenAsync(seite, "anmeldelink", new Dictionary<string, string> { ["Eingabe.Email"] = "x@example.de" })).Status);
        }

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], status);
        Assert.Equal(HttpStatusCode.OK, (await browser.LadenAsync("/konto/link")).Status); // GET bleibt erlaubt
    }
}
