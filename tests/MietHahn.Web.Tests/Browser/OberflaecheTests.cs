namespace MietHahn.Web.Tests.Browser;

public class OberflaecheTests(PostgresFixture postgres) : BrowserTestBasis(postgres)
{
    [Fact]
    public async Task Seiten_sind_auf_360_Pixeln_ohne_horizontales_Scrollen_bedienbar()
    {
        var seite = await AlsAdminAngemeldetAsync();

        foreach (var pfad in new[] { "/verwaltung", "/verwaltung/benutzer", "/verwaltung/system/mails", "/verwaltung/system/jobs", "/konto/sicherheit" })
        {
            await seite.GotoAsync(BasisUrl + pfad);
            await KeinHorizontalesScrollenAsync(seite);
        }

        var anonym = await NeueSeiteAsync();
        foreach (var pfad in new[] { "/konto/anmelden", "/konto/link", "/konto/passwort-vergessen" })
        {
            await anonym.GotoAsync(BasisUrl + pfad);
            await KeinHorizontalesScrollenAsync(anonym);
        }
    }

    [Fact]
    public async Task Mobil_oeffnet_das_Menue_ohne_JavaScript_und_navigiert()
    {
        var seite = await AlsAdminAngemeldetAsync();

        Assert.False(await seite.IsVisibleAsync(".seitenleiste"));
        Assert.False(await seite.IsVisibleAsync(".navigation-mobil .navigation"));

        await seite.ClickAsync(".navigation-mobil > summary");
        Assert.True(await seite.IsVisibleAsync(".navigation-mobil .navigation"));

        await seite.ClickAsync(".navigation-mobil >> text=Benutzer");
        await seite.WaitForURLAsync("**/verwaltung/benutzer");
        await seite.WaitForSelectorAsync("h1:text-is('Benutzer')");
    }

    [Fact]
    public async Task Desktop_zeigt_die_Seitenleiste_mit_markierter_Seite()
    {
        var seite = await AlsAdminAngemeldetAsync(mobil: false);
        await seite.GotoAsync(BasisUrl + "/verwaltung/benutzer");

        Assert.True(await seite.IsVisibleAsync(".seitenleiste"));
        Assert.False(await seite.IsVisibleAsync(".navigation-mobil > summary"));
        Assert.Equal("Benutzer", await seite.TextContentAsync(".seitenleiste a.active"));
    }

    [Fact]
    public async Task Formulare_funktionieren_mit_Enhanced_Forms_im_Browser()
    {
        var seite = await AlsAdminAngemeldetAsync();
        await seite.GotoAsync(BasisUrl + "/verwaltung/benutzer");

        await seite.FillAsync("#name", "Erika Mustermann");
        await seite.FillAsync("#email", "erika@example.de");
        await seite.SelectOptionAsync("#rolle", "Mieter");
        await seite.ClickAsync("text=Einladung senden");

        await seite.WaitForSelectorAsync(".meldung-ok:has-text('erika@example.de')");
        Assert.Contains("Erika Mustermann", await seite.TextContentAsync("table.tabelle"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Benutzermenue_bietet_Konto_und_Abmelden()
    {
        var seite = await AlsAdminAngemeldetAsync();

        await seite.ClickAsync(".benutzermenue > summary");
        Assert.Equal("Andy Hahn", (await seite.TextContentAsync(".benutzername"))!.Trim());
        await seite.ClickAsync(".benutzermenue >> text=Abmelden");

        await seite.WaitForURLAsync("**/konto/anmelden?abgemeldet=true");
        Assert.Contains("Sie wurden abgemeldet.", await seite.TextContentAsync("body"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sperren_verlangt_eine_Bestaetigung()
    {
        var seite = await AlsAdminAngemeldetAsync();
        await MitScreenshotBeiFehlerAsync(seite, async () =>
        {
            await seite.GotoAsync(BasisUrl + "/verwaltung/benutzer");
            await seite.FillAsync("#name", "Mieter Test");
            await seite.FillAsync("#email", "m@example.de");
            await seite.ClickAsync("text=Einladung senden");
            await seite.WaitForSelectorAsync(".meldung-ok:has-text('m@example.de')");

            await seite.ClickAsync("table.tabelle a:text-is('Mieter Test')");
            await seite.WaitForSelectorAsync("h1:text-is('Mieter Test')");

            Assert.False(await seite.IsVisibleAsync("text=Ja, Konto sperren"));
            await seite.ClickAsync("summary:has-text('Konto sperren')");
            await seite.ClickAsync("button:has-text('Ja, Konto sperren')");

            await seite.WaitForSelectorAsync(".meldung-ok:has-text('Das Konto wurde gesperrt.')");
        });
    }
}
