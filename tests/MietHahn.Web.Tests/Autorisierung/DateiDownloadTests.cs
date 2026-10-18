using System.Net;
using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Dateien;
using MietHahn.Application.Konten;
using MietHahn.Web.Tests.Konto;

namespace MietHahn.Web.Tests.Autorisierung;

public class DateiDownloadTests(PostgresFixture postgres) : KontoTestBasis(postgres)
{
    private static readonly HashSet<Guid> FreigegebeneDateien = [];

    protected override void DiensteAnpassen(IServiceCollection dienste) => dienste.AddScoped<IDateiFreigabe, TestFreigabe>();

    [Fact]
    public async Task Verwaltung_laedt_Dateien_mit_sicheren_Headern()
    {
        var id = await DateiAblegenAsync();
        var (admin, _) = await AngemeldetAlsAsync(Rollen.Admin);
        using var _ = admin;

        using var antwort = await admin.Http.GetAsync(new Uri($"/dateien/{id}", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        Assert.Equal("application/pdf", antwort.Content.Headers.ContentType!.MediaType);
        Assert.Equal("nosniff", antwort.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Contains("sandbox", antwort.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
        Assert.StartsWith("%PDF", await antwort.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);

        using var download = await admin.Http.GetAsync(new Uri($"/dateien/{id}?download=true", UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal("Beleg.pdf", download.Content.Headers.ContentDisposition!.FileNameStar ?? download.Content.Headers.ContentDisposition.FileName);
    }

    [Fact]
    public async Task Mieter_ohne_Freigabe_erhaelt_keinen_Zugriff()
    {
        var id = await DateiAblegenAsync();
        var (mieter, _) = await AngemeldetAlsAsync(Rollen.Mieter);
        using var _ = mieter;

        var antwort = await mieter.LadenAsync($"/dateien/{id}", weiterleitungenFolgen: false);

        Assert.Equal("/konto/zugriff-verweigert", antwort.Weiterleitung!.AbsolutePath);
    }

    [Fact]
    public async Task Mieter_mit_Freigabe_eines_Fachmoduls_erhaelt_Zugriff()
    {
        var id = await DateiAblegenAsync();
        FreigegebeneDateien.Add(id);
        var (mieter, _) = await AngemeldetAlsAsync(Rollen.Mieter);
        using var _ = mieter;

        Assert.Equal(HttpStatusCode.OK, (await mieter.LadenAsync($"/dateien/{id}", weiterleitungenFolgen: false)).Status);
    }

    [Fact]
    public async Task Ohne_Anmeldung_geht_es_zur_Anmeldung_und_unbekannte_Dateien_fehlen()
    {
        var id = await DateiAblegenAsync();
        using var anonym = App.Browser();
        Assert.Equal("/konto/anmelden", (await anonym.LadenAsync($"/dateien/{id}", weiterleitungenFolgen: false)).Weiterleitung!.AbsolutePath);

        var (admin, _) = await AngemeldetAlsAsync(Rollen.Admin);
        using var _ = admin;
        Assert.Equal(HttpStatusCode.NotFound, (await admin.LadenAsync($"/dateien/{Guid.CreateVersion7()}", weiterleitungenFolgen: false)).Status);
    }

    private Task<Guid> DateiAblegenAsync() => App.MitScopeAsync(async sp =>
    {
        using var pdf = new MemoryStream("%PDF-1.4\n%%EOF\n"u8.ToArray());
        return (await sp.GetRequiredService<IDateiablage>().SpeichernAsync(pdf, "Beleg.pdf", TestContext.Current.CancellationToken)).Id;
    });

    private sealed class TestFreigabe : IDateiFreigabe
    {
        public Task<bool> IstFreigegebenAsync(Guid dateiId, ClaimsPrincipal benutzer, CancellationToken cancellationToken = default) =>
            Task.FromResult(benutzer.IsInRole(Rollen.Mieter) && FreigegebeneDateien.Contains(dateiId));
    }
}
