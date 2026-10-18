using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Dokumente;
using MietHahn.Domain.Verwaltung;
using MietHahn.Infrastructure.Pdf;
using MietHahn.Infrastructure.Persistenz;
using MietHahn.Integration.Tests.Infrastruktur;
using UglyToad.PdfPig;

namespace MietHahn.Integration.Tests.Pdf;

public class PdfErzeugungTests(PostgresFixture postgres, GotenbergFixture gotenberg) : IClassFixture<GotenbergFixture>, IAsyncLifetime
{
    private const double PunkteProMm = 72 / 25.4;
    private const double A4HoeheMm = 297;

    private static readonly Briefanschrift Erika = new(["Erika Mustermann", "Hauptstraße 5, EG links", "12345 Musterstadt"], "Persönlich");

    private InfrastrukturUmgebung umgebung = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        umgebung = await InfrastrukturUmgebung.ErstellenAsync(postgres, new Dictionary<string, string?> { ["Pdf:GotenbergUrl"] = gotenberg.Url });
        await using var scope = umgebung.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MietHahnDbContext>();
        db.Add(new Vermieterprofil("Andy Hahn", "Musterweg 1", "12345", "Musterstadt")
        {
            Email = "vermieter@example.de",
            Iban = "DE02 1203 0000 0000 2020 51",
            Bank = "Musterbank",
        });
        await db.SaveChangesAsync(Ct);
    }

    public async ValueTask DisposeAsync() => await umgebung.DisposeAsync();

    [Fact]
    public async Task Brief_HTML_enthaelt_Briefkopf_Anschriftfeld_und_Fusszeile()
    {
        var (html, fusszeile) = await HtmlAsync(new PdfAuftrag("test", "Testbrief <1>", TestModell(), Erika, Datum: new DateOnly(2026, 10, 18)));

        Assert.Contains("<title>Testbrief &lt;1&gt;</title>", html, StringComparison.Ordinal);
        Assert.Contains("class=\"anschrift\"", html, StringComparison.Ordinal);
        Assert.Contains("Andy Hahn · Musterweg 1 · 12345 Musterstadt", html, StringComparison.Ordinal);
        Assert.Contains("<div>Hauptstraße 5, EG links</div>", html, StringComparison.Ordinal);
        Assert.Contains("Persönlich", html, StringComparison.Ordinal);
        Assert.Contains("Datum: 18.10.2026", html, StringComparison.Ordinal);
        Assert.Contains("1.234,56 €", html, StringComparison.Ordinal);
        Assert.Contains("IBAN DE02 1203 0000 0000 2020 51", fusszeile, StringComparison.Ordinal);
        Assert.Contains("<span class=\"pageNumber\"></span>", fusszeile, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dokument_ohne_Empfaenger_hat_kein_Anschriftfeld()
    {
        var (html, _) = await HtmlAsync(new PdfAuftrag("test", "Anlage", TestModell()));

        Assert.DoesNotContain("class=\"anschrift\"", html, StringComparison.Ordinal);
        Assert.Contains("<h1>Anlage</h1>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Vorlagentext_kann_direkt_gerendert_werden()
    {
        var (html, _) = await HtmlAsync(new PdfAuftrag(null, "Freitext", new { Name = "<Mieter>" }, VorlagenText: "<p>Hallo {{ name }}</p>"));

        Assert.Contains("<p>Hallo &lt;Mieter&gt;</p>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ohne_Vermieterprofil_gibt_es_eine_klare_Meldung()
    {
        await using var ohneProfil = await InfrastrukturUmgebung.ErstellenAsync(postgres);
        await using var scope = ohneProfil.Services.CreateAsyncScope();

        var fehler = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredService<IPdfErzeugung>().ErzeugenAsync(new PdfAuftrag("test", "x", TestModell()), Ct));
        Assert.Contains("Vermieterprofil", fehler.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Zu_viele_Anschriftzeilen_werden_abgelehnt()
    {
        var anschrift = new Briefanschrift(["1", "2", "3", "4", "5", "6", "7"]);

        await Assert.ThrowsAsync<ArgumentException>(() => HtmlAsync(new PdfAuftrag("test", "x", TestModell(), anschrift)));
    }

    [Fact]
    public async Task Brief_wird_als_PDF_A_mit_Anschrift_im_Fensterbereich_erzeugt()
    {
        var pdf = await PdfAsync(new PdfAuftrag("test", "Testbrief", TestModell(), Erika));

        Assert.True(pdf.AsSpan().StartsWith("%PDF-"u8));
        Assert.Contains("pdfaid:part>2<", System.Text.Encoding.ASCII.GetString(pdf), StringComparison.Ordinal);

        using var dokument = PdfDocument.Open(pdf);
        Assert.Equal("Testbrief", dokument.Information.Title);
        Assert.Equal(1, dokument.NumberOfPages);
        var seite = dokument.GetPage(1);
        Assert.Contains("Testdokument", seite.Text, StringComparison.Ordinal);
        Assert.Contains("Seite 1 von 1", seite.Text, StringComparison.Ordinal);

        // DIN 5008 Form B: Empfänger im Bereich 20–105 mm von links, 62,7–90 mm von oben.
        var erika = seite.GetWords().First(w => w.Text == "Erika");
        var links = erika.BoundingBox.Left / PunkteProMm;
        var vonOben = A4HoeheMm - (erika.BoundingBox.Top / PunkteProMm);
        Assert.InRange(links, 20, 25);
        Assert.InRange(vonOben, 62.7, 90);

        // Rücksendezeile oberhalb des Empfängers (45–50 mm von oben).
        var ruecksende = seite.GetWords().Where(w => w.Text == "Musterweg").MinBy(w => w.BoundingBox.Height)!;
        Assert.InRange(A4HoeheMm - (ruecksende.BoundingBox.Top / PunkteProMm), 45, 50);
    }

    [Fact]
    public async Task Lange_Dokumente_umbrechen_mit_Seitenzahlen()
    {
        var positionen = Enumerable.Range(1, 120).Select(i => new { Bezeichnung = $"Position {i}", Betrag = i * 1.5m }).ToList();

        var pdf = await PdfAsync(new PdfAuftrag("test", "Lang", new { Positionen = positionen, Summe = positionen.Sum(p => p.Betrag) }, Erika));

        using var dokument = PdfDocument.Open(pdf);
        Assert.True(dokument.NumberOfPages >= 3, $"Erwartet mindestens 3 Seiten, erhalten {dokument.NumberOfPages}.");
        Assert.Contains($"Seite 2 von {dokument.NumberOfPages}", dokument.GetPage(2).Text, StringComparison.Ordinal);
        Assert.Contains("Position", dokument.GetPage(2).Text, StringComparison.Ordinal); // Tabellenkopf wiederholt sich
        Assert.Contains("Position 120", dokument.GetPage(dokument.NumberOfPages).Text, StringComparison.Ordinal);

        // Die Summenzeile steht nur auf der letzten Seite.
        var seitenMitSumme = Enumerable.Range(1, dokument.NumberOfPages).Where(i => dokument.GetPage(i).GetWords().Any(w => w.Text == "Summe")).ToList();
        Assert.Equal([dokument.NumberOfPages], seitenMitSumme);
    }

    [Fact]
    public async Task PDF_wird_in_der_Dateiablage_abgelegt()
    {
        await using var scope = umgebung.Services.CreateAsyncScope();

        var datei = await scope.ServiceProvider.GetRequiredService<IPdfErzeugung>()
            .ErzeugenUndAblegenAsync(new PdfAuftrag("test", "Ablage", TestModell(), Erika), "Testbrief.pdf", Ct);

        Assert.Equal("application/pdf", datei.MimeTyp);
        Assert.Equal("Testbrief.pdf", datei.Name);
        Assert.True(datei.Groesse > 1000);
    }

    private static object TestModell() => new
    {
        Positionen = new[] { new { Bezeichnung = "Grundsteuer", Betrag = 1234.56m } },
        Summe = 1234.56m,
    };

    private async Task<(string Html, string Fusszeile)> HtmlAsync(PdfAuftrag auftrag)
    {
        await using var scope = umgebung.Services.CreateAsyncScope();
        var erzeugung = (PdfErzeugung)scope.ServiceProvider.GetRequiredService<IPdfErzeugung>();
        var (html, fusszeile, _) = await erzeugung.HtmlErzeugenAsync(auftrag, Ct);
        return (html, fusszeile);
    }

    private async Task<byte[]> PdfAsync(PdfAuftrag auftrag)
    {
        await using var scope = umgebung.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IPdfErzeugung>().ErzeugenAsync(auftrag, Ct);
    }
}
