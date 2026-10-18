using System.Net;
using MietHahn.Application.Konten;
using MietHahn.Web.Tests.Konto;

namespace MietHahn.Web.Tests.Autorisierung;

public class BereicheTests(PostgresFixture postgres) : KontoTestBasis(postgres)
{
    private static readonly string[] Pfade = ["/verwaltung", "/verwaltung/benutzer", "/verwaltung/system/jobs", "/service", "/portal"];

    [Theory]
    [InlineData(Rollen.Admin, "/verwaltung", "/verwaltung,/verwaltung/benutzer,/verwaltung/system/jobs")]
    [InlineData(Rollen.Verwalter, "/verwaltung", "/verwaltung")]
    [InlineData(Rollen.Hausmeister, "/service", "/service")]
    [InlineData(Rollen.Handwerker, "/service", "/service")]
    [InlineData(Rollen.Mieter, "/portal", "/portal")]
    public async Task Jede_Rolle_erreicht_genau_ihre_Bereiche(string rolle, string startseite, string erlaubt)
    {
        var (browser, _) = await AngemeldetAlsAsync(rolle);
        using var _ = browser;

        Assert.Equal(startseite, (await browser.LadenAsync("/")).Pfad);

        var erlaubtePfade = erlaubt.Split(',');
        foreach (var pfad in Pfade)
        {
            var antwort = await browser.LadenAsync(pfad, weiterleitungenFolgen: false);
            if (erlaubtePfade.Contains(pfad))
            {
                Assert.True(antwort.Status == HttpStatusCode.OK, $"{rolle} sollte {pfad} sehen (Status {antwort.Status}).");
            }
            else
            {
                Assert.True(
                    antwort.Status == HttpStatusCode.Redirect && antwort.Weiterleitung!.AbsolutePath == "/konto/zugriff-verweigert",
                    $"{rolle} darf {pfad} nicht sehen (Status {antwort.Status}, Ziel {antwort.Weiterleitung}).");
            }
        }
    }

    [Fact]
    public async Task Verwalter_mit_Recht_Benutzerverwaltung_darf_Benutzer_verwalten_aber_nicht_das_System()
    {
        var (browser, _) = await AngemeldetAlsAsync(Rollen.Verwalter, [Recht.Stammdaten, Recht.Benutzerverwaltung]);
        using var _ = browser;

        Assert.Equal(HttpStatusCode.OK, (await browser.LadenAsync("/verwaltung/benutzer", weiterleitungenFolgen: false)).Status);
        Assert.Equal(HttpStatusCode.Redirect, (await browser.LadenAsync("/verwaltung/system/jobs", weiterleitungenFolgen: false)).Status);
    }
}
