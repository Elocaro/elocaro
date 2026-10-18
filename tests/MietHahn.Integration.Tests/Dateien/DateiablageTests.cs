using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Dateien;
using MietHahn.Domain.Dateien;
using MietHahn.Infrastructure.Persistenz;
using MietHahn.Integration.Tests.Infrastruktur;
using SkiaSharp;

namespace MietHahn.Integration.Tests.Dateien;

public class DateiablageTests(PostgresFixture postgres) : IAsyncLifetime
{
    private InfrastrukturUmgebung umgebung = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() =>
        umgebung = await InfrastrukturUmgebung.ErstellenAsync(postgres, new Dictionary<string, string?>
        {
            ["Dateiablage:MaxGroesseMb"] = "1",
            ["Dateiablage:MaxBildKante"] = "100",
        });

    public async ValueTask DisposeAsync() => await umgebung.DisposeAsync();

    [Fact]
    public async Task Pdf_wird_gespeichert_und_kann_gelesen_werden()
    {
        var inhalt = TestDateien.Pdf();

        var datei = await SpeichernAsync(inhalt, "Rechnung Stadtwerke.pdf");

        Assert.Equal("application/pdf", datei.MimeTyp);
        Assert.Equal("Rechnung Stadtwerke.pdf", datei.Name);
        Assert.Equal(inhalt.Length, datei.Groesse);
        Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(inhalt)), datei.Sha256);
        Assert.True(File.Exists(Path.Combine(umgebung.DateiPfad, datei.Sha256[..2], datei.Sha256)));

        Assert.Equal(inhalt, await LesenAsync(datei.Id));
    }

    [Fact]
    public async Task Unbekannte_Datei_liefert_null()
    {
        await using var scope = umgebung.Services.CreateAsyncScope();
        var ablage = scope.ServiceProvider.GetRequiredService<IDateiablage>();

        Assert.Null(await ablage.OeffnenAsync(Guid.CreateVersion7(), Ct));
    }

    [Theory]
    [InlineData(new byte[] { 0x4D, 0x5A, 0x90, 0x00 }, "programm.pdf")] // Windows-EXE, als PDF getarnt
    [InlineData(new byte[] { 0x3C, 0x68, 0x74, 0x6D, 0x6C, 0x3E }, "seite.html")]
    public async Task Nicht_erlaubter_Typ_wird_am_Inhalt_erkannt_und_abgelehnt(byte[] inhalt, string name)
    {
        var fehler = await Assert.ThrowsAsync<DateiAbgelehntException>(() => SpeichernAsync(inhalt, name));

        Assert.Contains("nicht erlaubt", fehler.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(umgebung.DateiPfad) && Directory.EnumerateFiles(umgebung.DateiPfad, "*", SearchOption.AllDirectories).Any());
    }

    [Fact]
    public async Task Leere_Datei_wird_abgelehnt()
    {
        await Assert.ThrowsAsync<DateiAbgelehntException>(() => SpeichernAsync([], "leer.pdf"));
    }

    [Fact]
    public async Task Zu_grosse_Datei_wird_abgelehnt()
    {
        var gross = new byte[(1024 * 1024) + 1];
        TestDateien.Pdf().CopyTo(gross, 0);

        var fehler = await Assert.ThrowsAsync<DateiAbgelehntException>(() => SpeichernAsync(gross, "gross.pdf"));
        Assert.Contains("1 MB", fehler.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Gleicher_Inhalt_wird_nur_einmal_abgelegt()
    {
        var inhalt = TestDateien.Pdf("doppelt");

        var erste = await SpeichernAsync(inhalt, "a.pdf");
        var zweite = await SpeichernAsync(inhalt, "b.pdf");

        Assert.NotEqual(erste.Id, zweite.Id);
        Assert.Equal(erste.Sha256, zweite.Sha256);
        Assert.Single(Directory.EnumerateFiles(umgebung.DateiPfad, "*", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("../../etc/passwd.pdf", "passwd.pdf")]
    [InlineData("C:\\Users\\x\\Beleg.pdf", "Beleg.pdf")]
    [InlineData("", "datei.pdf")]
    [InlineData("rechnung.exe", "rechnung.pdf")]
    public async Task Dateiname_wird_bereinigt_und_Endung_folgt_dem_Inhalt(string name, string erwartet)
    {
        var datei = await SpeichernAsync(TestDateien.Pdf(name), name);

        Assert.Equal(erwartet, datei.Name);
    }

    [Fact]
    public async Task Grosses_Foto_wird_verkleinert_und_ohne_Metadaten_gespeichert()
    {
        var jpeg = TestDateien.MitExifAusrichtung(TestDateien.ZweifarbigesBild(400, 200), 1);

        var datei = await SpeichernAsync(jpeg, "foto.png");
        var gespeichert = await LesenAsync(datei.Id);

        Assert.Equal("image/jpeg", datei.MimeTyp);
        Assert.Equal("foto.jpg", datei.Name);
        Assert.Equal((100, 50), TestDateien.Groesse(gespeichert));
        Assert.DoesNotContain("Exif", System.Text.Encoding.ASCII.GetString(gespeichert), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Png_bleibt_Png()
    {
        var png = TestDateien.ZweifarbigesBild(40, 20, SKEncodedImageFormat.Png);

        var datei = await SpeichernAsync(png, "screenshot.png");

        Assert.Equal("image/png", datei.MimeTyp);
        Assert.Equal((40, 20), TestDateien.Groesse(await LesenAsync(datei.Id)));
    }

    /// <summary>
    /// Ausgangsbild 80×40: links rot, rechts blau. Erwartet wird, was ein Bildbetrachter nach Anwenden der
    /// EXIF-Ausrichtung anzeigt (geprüft an der Farbe oben links bzw. oben/unten).
    /// </summary>
    [Theory]
    [InlineData(1, 80, 40, "rot-links")]
    [InlineData(2, 80, 40, "blau-links")]  // gespiegelt
    [InlineData(3, 80, 40, "blau-links")]  // 180°
    [InlineData(4, 80, 40, "rot-links")]   // vertikal gespiegelt: links bleibt rot
    [InlineData(5, 40, 80, "rot-oben")]    // transponiert
    [InlineData(6, 40, 80, "rot-oben")]    // 90° im Uhrzeigersinn: linke Kante wird oben
    [InlineData(7, 40, 80, "blau-oben")]   // transversal
    [InlineData(8, 40, 80, "blau-oben")]   // 90° gegen den Uhrzeigersinn: linke Kante wird unten
    public async Task Exif_Ausrichtung_wird_angewendet(ushort ausrichtung, int breite, int hoehe, string erwartet)
    {
        var jpeg = TestDateien.MitExifAusrichtung(TestDateien.ZweifarbigesBild(80, 40), ausrichtung);

        var datei = await SpeichernAsync(jpeg, "foto.jpg");
        var bild = await LesenAsync(datei.Id);

        Assert.Equal((breite, hoehe), TestDateien.Groesse(bild));
        var (x, y) = erwartet.EndsWith("links", StringComparison.Ordinal) ? (0.1f, 0.5f) : (0.5f, 0.1f);
        var farbe = TestDateien.Farbe(bild, x, y);
        Assert.True(erwartet.StartsWith("rot", StringComparison.Ordinal) ? TestDateien.IstRot(farbe) : TestDateien.IstBlau(farbe), $"Farbe {farbe} passt nicht zu '{erwartet}'.");
    }

    [Fact]
    public async Task Beschaedigtes_Bild_wird_abgelehnt()
    {
        byte[] kaputt = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x02];

        await Assert.ThrowsAsync<DateiAbgelehntException>(() => SpeichernAsync(kaputt, "kaputt.jpg"));
    }

    [Fact]
    public async Task Metadaten_werden_in_der_Datenbank_gespeichert()
    {
        var datei = await SpeichernAsync(TestDateien.Pdf(), "beleg.pdf");

        await using var scope = umgebung.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MietHahnDbContext>();
        var gelesen = await db.Set<Datei>().SingleAsync(d => d.Id == datei.Id, Ct);
        Assert.Equal(datei.Sha256, gelesen.Sha256);
        Assert.Equal(InfrastrukturUmgebung.Startzeit, gelesen.ErstelltAm);
    }

    private async Task<Datei> SpeichernAsync(byte[] inhalt, string name)
    {
        await using var scope = umgebung.Services.CreateAsyncScope();
        var ablage = scope.ServiceProvider.GetRequiredService<IDateiablage>();
        using var stream = new MemoryStream(inhalt);
        return await ablage.SpeichernAsync(stream, name, Ct);
    }

    private async Task<byte[]> LesenAsync(Guid id)
    {
        await using var scope = umgebung.Services.CreateAsyncScope();
        var ablage = scope.ServiceProvider.GetRequiredService<IDateiablage>();
        await using var geoeffnet = await ablage.OeffnenAsync(id, Ct) ?? throw new InvalidOperationException("Datei fehlt.");
        using var puffer = new MemoryStream();
        await geoeffnet.Inhalt.CopyToAsync(puffer, Ct);
        return puffer.ToArray();
    }
}
