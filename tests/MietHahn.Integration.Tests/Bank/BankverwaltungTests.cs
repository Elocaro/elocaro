using System.Text;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Bank;
using MietHahn.Application.Gemeinsam;
using MietHahn.Application.Objekte;
using MietHahn.Domain.Bank;
using MietHahn.Integration.Tests.Infrastruktur;

namespace MietHahn.Integration.Tests.Bank;

public class BankverwaltungTests(PostgresFixture postgres) : IAsyncLifetime
{
    private InfrastrukturUmgebung umgebung = null!;
    private Guid haus;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        umgebung = await InfrastrukturUmgebung.ErstellenAsync(postgres);
        await using var scope = umgebung.Services.CreateAsyncScope();
        haus = Pruefen(await scope.ServiceProvider.GetRequiredService<IObjektVerwaltung>().AnlegenAsync(new ObjektDaten { Bezeichnung = "Lindenweg 3", Strasse = "Lindenweg 3", Plz = "50667", Ort = "Köln" }, Ct));
    }

    public async ValueTask DisposeAsync() => await umgebung.DisposeAsync();

    [Fact]
    public async Task Konto_mit_Objekten_und_gueltiger_eindeutiger_IBAN()
    {
        var id = Pruefen(await MitAsync(b => b.KontoAnlegenAsync(new BankkontoDaten { Name = "Mietkonto", Iban = "de89 3704 0044 0532 0130 00", Bic = "cobadeffxxx", ObjektIds = [haus] }, Ct)));

        var konto = Assert.Single(await MitAsync(b => b.KontenAsync(Ct)));
        Assert.Equal(("Mietkonto", "DE89 3704 0044 0532 0130 00", Kontotyp.Mietkonto), (konto.Name, konto.Iban, konto.Typ));
        Assert.Equal(["Lindenweg 3"], konto.Objekte);
        Assert.Equal("COBADEFFXXX", (await MitAsync(b => b.KontoLadenAsync(id, Ct)))!.Bic);

        var ungueltig = await MitAsync(b => b.KontoAnlegenAsync(new BankkontoDaten { Name = "Falsch", Iban = "DE89370400440532013001" }, Ct));
        Assert.Contains("Prüfsumme", ungueltig.Fehler.Single(), StringComparison.Ordinal);
        var doppelt = await MitAsync(b => b.KontoAnlegenAsync(new BankkontoDaten { Name = "Nochmal", Iban = "DE89370400440532013000" }, Ct));
        Assert.False(doppelt.Erfolgreich);
    }

    [Fact]
    public async Task Doppelter_Import_derselben_Datei_erzeugt_keine_Duplikate()
    {
        var konto = await KontoAsync();
        var datei = Testdatei("camt053_v02.xml");

        var erste = Pruefen(await MitAsync(b => b.VorschauAnlegenAsync(konto, "auszug.xml", datei, null, Ct)));
        var vorschau = (await MitAsync(b => b.VorschauAsync(erste, Ct)))!;
        Assert.Equal((Bankformat.Camt053, 4, 0), (vorschau.Format, vorschau.Zeilen.Count, vorschau.Zeilen.Count(z => z.Duplikat)));
        Assert.Null(vorschau.AbweichendeIban);
        Pruefen(await MitAsync(b => b.ImportierenAsync(erste, Ct)));
        Assert.False((await MitAsync(b => b.ImportierenAsync(erste, Ct))).Erfolgreich);

        var zweite = Pruefen(await MitAsync(b => b.VorschauAnlegenAsync(konto, "auszug.xml", datei, null, Ct)));
        Assert.All((await MitAsync(b => b.VorschauAsync(zweite, Ct)))!.Zeilen, z => Assert.True(z.Duplikat));
        Pruefen(await MitAsync(b => b.ImportierenAsync(zweite, Ct)));

        Assert.Equal(4, (await MitAsync(b => b.UmsaetzeAsync(new UmsatzSuche(konto), Ct))).Count);
        var protokoll = await MitAsync(b => b.ImporteAsync(konto, Ct));
        Assert.Equal([(4, 0, 4), (4, 4, 0)], protokoll.Select(p => (p.Gelesen, p.Duplikate, p.Neu)));
        Assert.Equal((new DateOnly(2026, 2, 2), new DateOnly(2026, 2, 6)), (protokoll[1].Ab!.Value, protokoll[1].Bis!.Value));
        Assert.All(protokoll, p => Assert.Equal(Importstatus.Importiert, p.Status));
    }

    [Fact]
    public async Task Datei_eines_anderen_Kontos_wird_gemeldet_und_nicht_importiert()
    {
        var anderes = Pruefen(await MitAsync(b => b.KontoAnlegenAsync(new BankkontoDaten { Name = "Rücklage", Iban = "DE02120300000000202051", Typ = Kontotyp.Ruecklage }, Ct)));

        var id = Pruefen(await MitAsync(b => b.VorschauAnlegenAsync(anderes, "auszug.sta", Testdatei("mt940.sta"), null, Ct)));

        Assert.Null((await MitAsync(b => b.VorschauAsync(id, Ct)))!.AbweichendeIban); // MT940 mit BLZ/Konto ohne IBAN
        var camt = Pruefen(await MitAsync(b => b.VorschauAnlegenAsync(anderes, "auszug.xml", Testdatei("camt053_v08.xml"), null, Ct)));
        Assert.Equal("DE89 3704 0044 0532 0130 00", (await MitAsync(b => b.VorschauAsync(camt, Ct)))!.AbweichendeIban);
        var ergebnis = await MitAsync(b => b.ImportierenAsync(camt, Ct));
        Assert.Contains("gehört zum Konto DE89", ergebnis.Fehler.Single(), StringComparison.Ordinal);

        Pruefen(await MitAsync(b => b.VerwerfenAsync(camt, Ct)));
        Assert.Null(await MitAsync(b => b.VorschauAsync(camt, Ct)));
    }

    [Fact]
    public async Task Unlesbare_Datei_wird_abgelehnt()
    {
        var konto = await KontoAsync();

        var ergebnis = await MitAsync(b => b.VorschauAnlegenAsync(konto, "bild.png", [0x89, 0x50, 0x4E, 0x47], null, Ct));

        Assert.Contains("nicht erkannt", ergebnis.Fehler.Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Umsaetze_suchen_ignorieren_und_wieder_oeffnen()
    {
        var konto = await KontoAsync();
        var import = Pruefen(await MitAsync(b => b.VorschauAnlegenAsync(konto, "sparkasse.csv", Testdatei("sparkasse.csv"), null, Ct)));
        Assert.Equal("Sparkasse (CSV-CAMT)", (await MitAsync(b => b.VorschauAsync(import, Ct)))!.Profil);
        Pruefen(await MitAsync(b => b.ImportierenAsync(import, Ct)));

        var strom = Assert.Single(await MitAsync(b => b.UmsaetzeAsync(new UmsatzSuche(Text: "müllerstadt"), Ct)));
        Assert.Equal(Umsatzstatus.Offen, strom.Status);
        Assert.Single(await MitAsync(b => b.UmsaetzeAsync(new UmsatzSuche(Text: "DE9337"), Ct)));

        Pruefen(await MitAsync(b => b.IgnorierenAsync(strom.Id, "Hausstrom, wird über Belege erfasst", Ct)));
        Assert.Single(await MitAsync(b => b.UmsaetzeAsync(new UmsatzSuche(konto, Umsatzstatus.Offen), Ct)));
        var ignoriert = Assert.Single(await MitAsync(b => b.UmsaetzeAsync(new UmsatzSuche(konto, Umsatzstatus.Ignoriert), Ct)));
        Assert.Equal("Hausstrom, wird über Belege erfasst", ignoriert.Notiz);
        Assert.Equal(1, Assert.Single(await MitAsync(b => b.KontenAsync(Ct))).OffeneUmsaetze);

        Pruefen(await MitAsync(b => b.WiederOeffnenAsync(strom.Id, Ct)));
        Assert.Equal(2, (await MitAsync(b => b.UmsaetzeAsync(new UmsatzSuche(konto, Umsatzstatus.Offen), Ct))).Count);
    }

    [Fact]
    public async Task Eigenes_CSV_Profil_mit_Soll_und_Haben_Spalten()
    {
        var konto = await KontoAsync();
        Pruefen(await MitAsync(b => b.CsvProfilSpeichernAsync(null, new CsvProfilDaten
        {
            Name = "Hausbank",
            Trennzeichen = ",",
            Dezimaltrennzeichen = ".",
            Datumsformat = "yyyy-MM-dd",
            SpalteBuchungsdatum = "Date",
            SpalteSoll = "Debit",
            SpalteHaben = "Credit",
            SpalteName = "Payee",
            SpalteVerwendungszweck = "Memo",
        }, Ct)));
        Assert.False((await MitAsync(b => b.CsvProfilSpeichernAsync(null, new CsvProfilDaten { Name = "DKB", SpalteBuchungsdatum = "x", SpalteBetrag = "y" }, Ct))).Erfolgreich);
        Assert.False((await MitAsync(b => b.CsvProfilSpeichernAsync(null, new CsvProfilDaten { Name = "Ohne Betrag", SpalteBuchungsdatum = "x" }, Ct))).Erfolgreich);

        var csv = Encoding.UTF8.GetBytes("Date,Payee,Memo,Debit,Credit\n2026-02-02,Anna Schmidt,\"Miete Februar, Wohnung 1\",,770.00\n2026-02-05,Stadtwerke,Strom,84.50,\n");
        var import = Pruefen(await MitAsync(b => b.VorschauAnlegenAsync(konto, "export.csv", csv, null, Ct)));
        var vorschau = (await MitAsync(b => b.VorschauAsync(import, Ct)))!;

        Assert.Equal("Hausbank", vorschau.Profil);
        Assert.Equal([(770m, "Miete Februar, Wohnung 1"), (-84.50m, "Strom")], vorschau.Zeilen.Select(z => (z.Umsatz.Betrag, z.Umsatz.Verwendungszweck)));
        Assert.Contains(await MitAsync(b => b.CsvProfileAsync(Ct)), p => p is { Name: "Hausbank", Vorgabe: false });
        Assert.Contains(await MitAsync(b => b.CsvProfileAsync(Ct)), p => p is { Name: "Sparkasse (CSV-CAMT)", Vorgabe: true });
    }

    private async Task<Guid> KontoAsync() =>
        Pruefen(await MitAsync(b => b.KontoAnlegenAsync(new BankkontoDaten { Name = "Mietkonto", Iban = "DE89370400440532013000" }, Ct)));

    private static byte[] Testdatei(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Bank", "Testdateien", name));

    private static Guid Pruefen(Ergebnis ergebnis)
    {
        Assert.True(ergebnis.Erfolgreich, string.Join(", ", ergebnis.Fehler));
        return ergebnis.Id!.Value;
    }

    private async Task<T> MitAsync<T>(Func<IBankverwaltung, Task<T>> aktion)
    {
        await using var scope = umgebung.Services.CreateAsyncScope();
        return await aktion(scope.ServiceProvider.GetRequiredService<IBankverwaltung>());
    }
}
