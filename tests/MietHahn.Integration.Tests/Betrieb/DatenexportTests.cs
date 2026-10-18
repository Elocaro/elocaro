using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Betrieb;
using MietHahn.Application.Dateien;
using MietHahn.Application.Konten;
using MietHahn.Application.Verwaltung;
using MietHahn.Domain.Dateien;
using MietHahn.Infrastructure.Audit;
using MietHahn.Infrastructure.Betrieb;
using MietHahn.Infrastructure.Identitaet;
using MietHahn.Infrastructure.Persistenz;
using MietHahn.Integration.Tests.Infrastruktur;

namespace MietHahn.Integration.Tests.Betrieb;

public class DatenexportTests(PostgresFixture postgres) : IAsyncLifetime
{
    private InfrastrukturUmgebung umgebung = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => umgebung = await InfrastrukturUmgebung.ErstellenAsync(postgres);

    public async ValueTask DisposeAsync() => await umgebung.DisposeAsync();

    [Fact]
    public async Task Export_enthaelt_jede_Tabelle_als_Json_und_Csv_mit_Manifest()
    {
        await VermieterAnlegenAsync();

        using var zip = await ExportierenAsync();

        Assert.NotNull(zip.GetEntry("LIESMICH.txt"));
        var manifest = Json(zip, "manifest.json");
        Assert.Equal("MietHahn-Vollexport", manifest.GetProperty("format").GetString());
        var tabellen = manifest.GetProperty("tabellen").EnumerateArray().Select(t => t.GetProperty("name").GetString()!).ToList();
        Assert.Contains("vermieterprofile", tabellen);
        Assert.Contains("benutzer", tabellen);
        Assert.Contains("audit_eintraege", tabellen);
        foreach (var tabelle in tabellen)
        {
            Assert.NotNull(zip.GetEntry($"daten/{tabelle}.json"));
            Assert.NotNull(zip.GetEntry($"daten/{tabelle}.csv"));
        }

        var vermieter = Json(zip, "daten/vermieterprofile.json").EnumerateArray().Single();
        Assert.Equal("Familie Müller-Lüdenscheidt", vermieter.GetProperty("name").GetString());
        Assert.Equal("DE02 1203 0000 0000 2020 51", vermieter.GetProperty("iban").GetString());
        Assert.False(vermieter.TryGetProperty("xmin", out _));
        Assert.Equal(1, manifest.GetProperty("tabellen").EnumerateArray().Single(t => t.GetProperty("name").GetString() == "vermieterprofile").GetProperty("zeilen").GetInt32());
    }

    [Fact]
    public async Task Csv_ist_fuer_deutsches_Excel_lesbar()
    {
        await VermieterAnlegenAsync();

        using var zip = await ExportierenAsync();

        var bytes = Bytes(zip, "daten/vermieterprofile.csv");
        Assert.Equal(Encoding.UTF8.Preamble.ToArray(), bytes[..3]);
        var zeilen = Encoding.UTF8.GetString(bytes[3..]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, zeilen.Length);
        var kopf = zeilen[0].Split(';');
        var werte = zeilen[1].Split(';');
        Assert.Equal("Familie Müller-Lüdenscheidt", werte[Array.IndexOf(kopf, "name")]);
        Assert.Equal("nein", werte[Array.IndexOf(kopf, "geloescht")]);
        Assert.Matches(@"^2026-03-01 10:30:00$", werte[Array.IndexOf(kopf, "erstellt_am")]);
    }

    [Fact]
    public async Task Geheimnisse_sind_nicht_enthalten()
    {
        await using (var scope = umgebung.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IKontoverwaltung>().ErstenAdminAnlegenAsync("admin@example.de", "Admin", "Langes-Passwort-2026", Ct);
            var benutzerManager = scope.ServiceProvider.GetRequiredService<UserManager<Benutzer>>();
            var admin = await benutzerManager.FindByEmailAsync("admin@example.de");
            await benutzerManager.ResetAuthenticatorKeyAsync(admin!);
            var db = scope.ServiceProvider.GetRequiredService<MietHahnDbContext>();
            Assert.True(await db.Set<IdentityUserToken<Guid>>().AnyAsync(Ct));
        }

        using var zip = await ExportierenAsync();

        var benutzer = Json(zip, "daten/benutzer.json").EnumerateArray().Single();
        Assert.Equal("admin@example.de", benutzer.GetProperty("email").GetString());
        Assert.False(benutzer.TryGetProperty("password_hash", out _));
        Assert.False(benutzer.TryGetProperty("security_stamp", out _));
        Assert.False(benutzer.TryGetProperty("concurrency_stamp", out _));
        Assert.Null(zip.GetEntry("daten/benutzer_tokens.json"));
        Assert.Null(zip.GetEntry("daten/data_protection_keys.json"));
        Assert.Null(zip.GetEntry("daten/anmeldelinks.json"));
        Assert.DoesNotContain(zip.Entries, e => Encoding.UTF8.GetString(Bytes(zip, e.FullName)).Contains("AQAAAA", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Jede_Tabelle_ist_exportiert_oder_bewusst_ausgenommen()
    {
        await using var scope = umgebung.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MietHahnDbContext>();

        var alle = db.Model.GetRelationalModel().Tables.Select(t => t.Name).ToHashSet();
        var exportiert = Datenexport.ExportTabellen(db.Model).Select(t => t.Name).ToHashSet();

        // Neue Tabellen landen automatisch im Export. Wer eine ausnimmt ([OhneExport]), muss sie hier eintragen.
        Assert.Equal(["anmeldelinks", "benutzer_tokens", "data_protection_keys"], alle.Except(exportiert).Order());
    }

    [Fact]
    public async Task Dateien_liegen_mit_Originalname_im_Export_auch_geloeschte()
    {
        var inhalt = TestDateien.Pdf();
        Guid id, geloeschtId;
        await using (var scope = umgebung.Services.CreateAsyncScope())
        {
            var ablage = scope.ServiceProvider.GetRequiredService<IDateiablage>();
            id = (await ablage.SpeichernAsync(new MemoryStream(inhalt), "Rechnung Stadtwerke März.pdf", Ct)).Id;
            var geloescht = await ablage.SpeichernAsync(new MemoryStream(TestDateien.Pdf("zweite")), "foto.pdf", Ct);
            geloeschtId = geloescht.Id;
            var db = scope.ServiceProvider.GetRequiredService<MietHahnDbContext>();
            db.Remove(geloescht);
            await db.SaveChangesAsync(Ct);
        }

        using var zip = await ExportierenAsync();

        Assert.Equal(inhalt, Bytes(zip, $"dateien/{id}/Rechnung Stadtwerke März.pdf"));
        Assert.NotNull(zip.GetEntry($"dateien/{geloeschtId}/foto.pdf"));
        var zeile = Json(zip, "daten/dateien.json").EnumerateArray().Single(d => d.GetProperty("id").GetGuid() == geloeschtId);
        Assert.True(zeile.GetProperty("geloescht").GetBoolean());
        Assert.Equal(2, Json(zip, "manifest.json").GetProperty("dateien").GetProperty("anzahl").GetInt32());
    }

    [Fact]
    public async Task Fehlende_Dateiinhalte_stehen_im_Manifest()
    {
        Datei datei;
        await using (var scope = umgebung.Services.CreateAsyncScope())
        {
            datei = await scope.ServiceProvider.GetRequiredService<IDateiablage>().SpeichernAsync(new MemoryStream(TestDateien.Pdf()), "weg.pdf", Ct);
        }

        File.Delete(Path.Combine(umgebung.DateiPfad, datei.Sha256[..2], datei.Sha256));
        using var zip = await ExportierenAsync();

        var dateien = Json(zip, "manifest.json").GetProperty("dateien");
        Assert.Equal(0, dateien.GetProperty("anzahl").GetInt32());
        Assert.Equal(datei.Id, dateien.GetProperty("fehlend").EnumerateArray().Single().GetGuid());
    }

    [Fact]
    public async Task Export_wird_im_Audit_Log_vermerkt()
    {
        await ExportierenAsync();

        await using var scope = umgebung.Services.CreateAsyncScope();
        var eintrag = await scope.ServiceProvider.GetRequiredService<MietHahnDbContext>().Set<AuditEintrag>().SingleAsync(a => a.EntitaetTyp == "Datenexport", Ct);
        Assert.Equal(Application.Audit.AuditAktion.Gelesen, eintrag.Aktion);
    }

    [Fact]
    public async Task Dateiname_enthaelt_das_deutsche_Datum()
    {
        umgebung.Uhr.SetUtcNow(new DateTimeOffset(2026, 10, 18, 23, 30, 0, TimeSpan.Zero));
        await using var scope = umgebung.Services.CreateAsyncScope();

        Assert.Equal("miethahn-export-2026-10-19.zip", scope.ServiceProvider.GetRequiredService<IDatenexport>().Dateiname());
    }

    [Theory]
    [InlineData("einfach", "einfach")]
    [InlineData("a;b", "\"a;b\"")]
    [InlineData("Sie sagte \"Hallo\"", "\"Sie sagte \"\"Hallo\"\"\"")]
    [InlineData("zwei\nZeilen", "\"zwei\nZeilen\"")]
    public void Csv_Felder_werden_bei_Bedarf_gequotet(string wert, string erwartet) => Assert.Equal(erwartet, Datenexport.CsvFeld(wert));

    [Theory]
    [InlineData("=HYPERLINK(\"http://x\")", "'=HYPERLINK(\"http://x\")")]
    [InlineData("+49 123", "'+49 123")]
    [InlineData("@SUMME(A1)", "'@SUMME(A1)")]
    [InlineData("Müller", "Müller")]
    public void Csv_verhindert_Formel_Injektion(string wert, string erwartet) => Assert.Equal(erwartet, Datenexport.CsvWert(wert, "text"));

    [Fact]
    public void Csv_formatiert_Betraege_mit_Dezimalkomma() => Assert.Equal("650,50", Datenexport.CsvWert(650.50m, "numeric(12,2)"));

    private async Task VermieterAnlegenAsync()
    {
        await using var scope = umgebung.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IVermieterprofilVerwaltung>().SpeichernAsync(
            new VermieterprofilDaten { Name = "Familie Müller-Lüdenscheidt", Strasse = "Hauptstraße 1", Plz = "12345", Ort = "Köln", Iban = "DE02120300000000202051" }, Ct);
    }

    private async Task<ZipArchive> ExportierenAsync()
    {
        var puffer = new MemoryStream();
        await using (var scope = umgebung.Services.CreateAsyncScope())
        {
            // Wie der Response-Body: nicht durchsuchbar, nur asynchron beschreibbar.
            await using var ziel = new NurAsynchronSchreibbar(puffer);
            await scope.ServiceProvider.GetRequiredService<IDatenexport>().ExportierenAsync(ziel, Ct);
        }

        puffer.Position = 0;
        return new ZipArchive(puffer, ZipArchiveMode.Read);
    }

    private static JsonElement Json(ZipArchive zip, string name) => JsonDocument.Parse(Bytes(zip, name)).RootElement;

    private static byte[] Bytes(ZipArchive zip, string name)
    {
        var eintrag = zip.GetEntry(name) ?? throw new InvalidOperationException($"{name} fehlt im Export.");
        using var strom = eintrag.Open();
        using var kopie = new MemoryStream();
        strom.CopyTo(kopie);
        return kopie.ToArray();
    }

    private sealed class NurAsynchronSchreibbar(Stream innen) : Stream
    {
        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush() => throw new InvalidOperationException("Synchrones Flush ist wie in Kestrel nicht erlaubt.");

        public override Task FlushAsync(CancellationToken cancellationToken) => innen.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Synchrones Schreiben ist wie in Kestrel nicht erlaubt.");

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => innen.WriteAsync(buffer, cancellationToken);

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => innen.WriteAsync(buffer, offset, count, cancellationToken);
    }
}
