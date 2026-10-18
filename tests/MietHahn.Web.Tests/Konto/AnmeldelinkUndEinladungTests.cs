using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Konten;
using MietHahn.Infrastructure.Identitaet;

namespace MietHahn.Web.Tests.Konto;

public class AnmeldelinkUndEinladungTests(PostgresFixture postgres) : KontoTestBasis(postgres)
{
    private const string MieterEmail = "mieterin@example.de";

    [Fact]
    public async Task Eingeladener_Mieter_meldet_sich_per_Link_an_und_der_Link_ist_danach_verbraucht()
    {
        Assert.True((await EinladenAsync(MieterEmail, Rollen.Mieter)).Erfolgreich);
        var mail = (await LetzteMailAnAsync(MieterEmail))!;
        Assert.Equal("Ihr Zugang zu MietHahn", mail.Betreff);
        var link = LinkAus(mail);
        Assert.Matches("^https://localhost/konto/link/[A-Za-z0-9_-]{43}$", link);

        using var browser = App.Browser();
        var seite = await browser.LadenAsync(link);
        Assert.Contains("Klicken Sie auf „Anmelden“", seite.Text, StringComparison.Ordinal);

        // Das bloße Öffnen (z. B. durch einen Mail-Scanner) verbraucht den Link nicht.
        var nochmalGeoeffnet = await browser.LadenAsync(link);
        var angemeldet = await browser.AbsendenAsync(nochmalGeoeffnet, "link-einloesen");
        Assert.Equal("/", angemeldet.Pfad);
        Assert.Equal(HttpStatusCode.OK, angemeldet.Status);

        using var zweiterBrowser = App.Browser();
        var erneut = await zweiterBrowser.AbsendenAsync(await zweiterBrowser.LadenAsync(link), "link-einloesen");
        Assert.Contains("ungültig, abgelaufen oder wurde bereits verwendet", erneut.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Angeforderter_Anmeldelink_laeuft_nach_15_Minuten_ab()
    {
        await EinladenAsync(MieterEmail, Rollen.Mieter);
        using var browser = App.Browser();
        var anfordern = await browser.LadenAsync("/konto/link");
        await browser.AbsendenAsync(anfordern, "anmeldelink", new Dictionary<string, string> { ["Eingabe.Email"] = MieterEmail });
        var mail = (await LetzteMailAnAsync(MieterEmail))!;
        Assert.Equal("Ihr Anmeldelink für MietHahn", mail.Betreff);

        App.Uhr.Advance(TimeSpan.FromMinutes(16));
        var seite = await browser.AbsendenAsync(await browser.LadenAsync(LinkAus(mail)), "link-einloesen");

        Assert.Contains("ungültig, abgelaufen", seite.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("unbekannt@example.de")]
    [InlineData(AdminEmail)]
    public async Task Anmeldelink_Anforderung_verraet_nichts_und_schickt_Admins_keinen_Link(string email)
    {
        using var browser = App.Browser();
        await EinrichtenAsync(browser);

        var anfordern = await browser.LadenAsync("/konto/link");
        var ergebnis = await browser.AbsendenAsync(anfordern, "anmeldelink", new Dictionary<string, string> { ["Eingabe.Email"] = email });

        Assert.Contains("Falls ein passendes Konto existiert", ergebnis.Text, StringComparison.Ordinal);
        Assert.Null(await LetzteMailAnAsync(email));
    }

    [Fact]
    public async Task Eingeladener_Verwalter_legt_Passwort_fest_und_muss_Zwei_Faktor_einrichten()
    {
        const string email = "partnerin@example.de";
        await EinladenAsync(email, Rollen.Verwalter);
        var mail = (await LetzteMailAnAsync(email))!;
        Assert.Equal("Einladung zu MietHahn", mail.Betreff);
        Assert.Contains("Verwalter", mail.Html, StringComparison.Ordinal);

        using var browser = App.Browser();
        var seite = await browser.LadenAsync(LinkAus(mail));
        var ergebnis = await browser.AbsendenAsync(seite, "einladung", new Dictionary<string, string>
        {
            ["Eingabe.Passwort"] = "Partnerin-Passwort-2026",
            ["Eingabe.PasswortWiederholung"] = "Partnerin-Passwort-2026",
        });

        Assert.Equal("/konto/sicherheit/zwei-faktor", ergebnis.Pfad);

        // Die Einladung lässt sich danach nicht erneut verwenden.
        using var andererBrowser = App.Browser();
        Assert.Contains("ungültig oder abgelaufen", (await andererBrowser.LadenAsync(LinkAus(mail))).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Manipulierte_Einladung_wird_abgelehnt()
    {
        const string email = "hausmeister@example.de";
        await EinladenAsync(email, Rollen.Hausmeister);
        var link = LinkAus((await LetzteMailAnAsync(email))!);
        using var browser = App.Browser();

        Assert.Contains("ungültig oder abgelaufen", (await browser.LadenAsync(link[..^4] + "AAAA")).Text, StringComparison.Ordinal);
        Assert.Contains("ungültig oder abgelaufen", (await browser.LadenAsync("/konto/einladung")).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Doppelte_Einladung_wird_abgelehnt()
    {
        await EinladenAsync(MieterEmail, Rollen.Mieter);

        var zweite = await EinladenAsync(MieterEmail, Rollen.Mieter);

        Assert.False(zweite.Erfolgreich);
        Assert.Contains("existiert bereits", zweite.Fehler[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Gesperrter_Mieter_kann_den_Link_nicht_nutzen()
    {
        await EinladenAsync(MieterEmail, Rollen.Mieter);
        var link = LinkAus((await LetzteMailAnAsync(MieterEmail))!);
        await App.MitScopeAsync(async sp =>
        {
            var manager = sp.GetRequiredService<UserManager<Benutzer>>();
            var benutzer = await manager.FindByEmailAsync(MieterEmail);
            benutzer!.Gesperrt = true;
            return await manager.UpdateAsync(benutzer);
        });

        using var browser = App.Browser();
        var seite = await browser.AbsendenAsync(await browser.LadenAsync(link), "link-einloesen");

        Assert.Contains("ungültig", seite.Text, StringComparison.Ordinal);
    }
}
