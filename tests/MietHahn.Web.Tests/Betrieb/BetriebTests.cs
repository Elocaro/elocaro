using System.Net;
using System.Text.Json;
using MietHahn.Web.Tests.Konto;

namespace MietHahn.Web.Tests.Betrieb;

public class BetriebTests(PostgresFixture postgres) : KontoTestBasis(postgres)
{
    // Gotenberg läuft in diesen Tests nicht: die Bereitschaftsprüfung muss das melden.
    protected override IDictionary<string, string?> Konfiguration => new Dictionary<string, string?>
    {
        ["Pdf:GotenbergUrl"] = "http://127.0.0.1:1/",
        ["Betrieb:HttpsUmleitung"] = "false", // wie im Container: TLS endet bei Caddy
    };

    [Fact]
    public async Task Lebt_Pruefung_ist_ohne_Anmeldung_erreichbar()
    {
        using var browser = App.Browser();

        Assert.Equal(HttpStatusCode.OK, (await browser.LadenAsync("/health/live", weiterleitungenFolgen: false)).Status);
    }

    [Fact]
    public async Task Bereitschaft_meldet_ausgefallene_Abhaengigkeiten_ohne_Details()
    {
        using var browser = App.Browser();

        var antwort = await browser.LadenAsync("/health", weiterleitungenFolgen: false);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, antwort.Status);
        using var json = JsonDocument.Parse(antwort.Html);
        var pruefungen = json.RootElement.GetProperty("pruefungen");
        Assert.Equal("Healthy", pruefungen.GetProperty("datenbank").GetString());
        Assert.Equal("Healthy", pruefungen.GetProperty("dateiablage").GetString());
        Assert.Equal("Unhealthy", pruefungen.GetProperty("gotenberg").GetString());
        Assert.DoesNotContain("127.0.0.1", antwort.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Antworten_tragen_Sicherheits_Header()
    {
        using var browser = App.Browser();

        using var antwort = await browser.Http.GetAsync(new Uri("/konto/anmelden", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal("DENY", antwort.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("nosniff", antwort.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("strict-origin-when-cross-origin", antwort.Headers.GetValues("Referrer-Policy").Single());
        var csp = antwort.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("script-src 'self'", csp, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", csp, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, "http://")]
    [InlineData("https", "https://")]
    public async Task Hinter_dem_Proxy_wird_das_Schema_aus_X_Forwarded_Proto_uebernommen(string? proto, string erwartet)
    {
        using var browser = App.Browser();
        using var anfrage = new HttpRequestMessage(HttpMethod.Get, "http://localhost/");
        if (proto is not null)
        {
            anfrage.Headers.Add("X-Forwarded-Proto", proto);
        }

        using var antwort = await browser.Http.SendAsync(anfrage, TestContext.Current.CancellationToken);

        // Die Anmelde-Weiterleitung nutzt das vom Proxy gemeldete Schema.
        Assert.Equal(HttpStatusCode.Redirect, antwort.StatusCode);
        Assert.StartsWith(erwartet, antwort.Headers.Location!.ToString(), StringComparison.Ordinal);
    }
}
