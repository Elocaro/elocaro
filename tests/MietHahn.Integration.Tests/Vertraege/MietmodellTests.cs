using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Aufgaben;
using MietHahn.Application.Gemeinsam;
using MietHahn.Application.Konten;
using MietHahn.Application.Objekte;
using MietHahn.Application.Personen;
using MietHahn.Application.Vertraege;
using MietHahn.Domain.Kommunikation;
using MietHahn.Domain.Vertraege;
using MietHahn.Infrastructure.Persistenz;
using MietHahn.Infrastructure.Vertraege;
using MietHahn.Integration.Tests.Infrastruktur;

namespace MietHahn.Integration.Tests.Vertraege;

public class MietmodellTests(PostgresFixture postgres) : IAsyncLifetime
{
    // Startzeit der Testumgebung: 01.03.2026
    private static readonly DateOnly Beginn = new(2026, 3, 1);
    private static readonly ClaimsPrincipal Admin = new(new ClaimsIdentity([new Claim(ClaimTypes.Role, Rollen.Admin)], "test"));

    private InfrastrukturUmgebung umgebung = null!;
    private Guid vertragId;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        umgebung = await InfrastrukturUmgebung.ErstellenAsync(postgres);
        await using var scope = umgebung.Services.CreateAsyncScope();
        var objekte = scope.ServiceProvider.GetRequiredService<IObjektVerwaltung>();
        var objekt = Pruefen(await objekte.AnlegenAsync(new ObjektDaten { Bezeichnung = "Lindenweg 3", Strasse = "Lindenweg 3", Plz = "50667", Ort = "Köln" }, Ct));
        var wohnung = Pruefen(await objekte.EinheitAnlegenAsync(objekt, new EinheitDaten { Bezeichnung = "Wohnung 1" }, Ct));
        var mieter = Pruefen(await scope.ServiceProvider.GetRequiredService<IPersonenVerwaltung>().AnlegenAsync(new PersonDaten { Vorname = "Anna", Nachname = "Schmidt" }, null, Ct));
        vertragId = Pruefen(await scope.ServiceProvider.GetRequiredService<IVertragsVerwaltung>().AnlegenAsync(
            new VertragAnlage { MietobjektId = wohnung, Beginn = Beginn, MieterIds = [mieter], Kaltmiete = 650m }, Ct));
        await scope.ServiceProvider.GetRequiredService<IKontoverwaltung>().ErstenAdminAnlegenAsync("admin@example.de", "Admin", "Langes-Passwort-2026", Ct);
    }

    public async ValueTask DisposeAsync() => await umgebung.DisposeAsync();

    [Fact]
    public async Task Staffel_wird_am_Stichtag_uebernommen_und_der_Admin_informiert()
    {
        Pruefen(await MitAsync(v => v.MietmodellSetzenAsync(vertragId, new MietmodellEingabe { Modell = Mietmodell.Staffel }, Ct)));
        Pruefen(await MitAsync(v => v.StaffelHinzufuegenAsync(vertragId, new StaffelEingabe { Ab = new DateOnly(2027, 3, 1), Kaltmiete = 700m }, Ct)));
        var zuFrueh = await MitAsync(v => v.StaffelHinzufuegenAsync(vertragId, new StaffelEingabe { Ab = new DateOnly(2027, 9, 1), Kaltmiete = 720m }, Ct));
        Assert.False(zuFrueh.Erfolgreich);

        umgebung.Uhr.SetUtcNow(new DateTimeOffset(2027, 2, 27, 23, 0, 0, TimeSpan.Zero)); // 28.02. deutsche Zeit
        Assert.Equal(0, await AnwendenAsync());
        Assert.Contains(await AufgabenAsync(), a => a.Titel.StartsWith("Staffel Wohnung 1: 700,00 € ab 01.03.2027", StringComparison.Ordinal));

        umgebung.Uhr.SetUtcNow(new DateTimeOffset(2027, 3, 1, 0, 30, 0, TimeSpan.Zero));
        Assert.Equal(1, await AnwendenAsync());
        Assert.Equal(0, await AnwendenAsync());

        var vertrag = (await MitAsync(v => v.LadenAsync(vertragId, Ct)))!;
        Assert.Equal(700m, vertrag.Mietmodell.Kaltmiete);
        Assert.NotNull(vertrag.Mietmodell.Staffeln.Single().AngewendetAm);
        Assert.Equal(new DateOnly(2027, 3, 1), vertrag.Mietmodell.LetzteErhoehung);

        await using var scope = umgebung.Services.CreateAsyncScope();
        var mail = await scope.ServiceProvider.GetRequiredService<MietHahnDbContext>().Set<AusgehendeMail>().SingleAsync(m => m.Vorlage == "staffel-angewendet", Ct);
        Assert.Equal(["admin@example.de"], mail.An);
        Assert.Contains("Wohnung 1, Lindenweg 3: ab 01.03.2027 700,00 € Kaltmiete", mail.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Indexmiete_mit_importiertem_VPI_berechnen_und_uebernehmen()
    {
        Pruefen(await MitAsync(v => v.MietmodellSetzenAsync(vertragId, new MietmodellEingabe { Modell = Mietmodell.Index, IndexBasisMonat = new DateOnly(2026, 1, 1), IndexBasisWert = 117.4m }, Ct)));
        await using (var scope = umgebung.Services.CreateAsyncScope())
        {
            var import = await scope.ServiceProvider.GetRequiredService<IVpiVerwaltung>().ImportierenAsync(
                "GENESIS-Tabelle: 61111-0002\nJahr;Monat;Verbraucherpreisindex\n2026;Januar;117,4\n2026;Februar;117,9\n2027;Januar;121,7\n2027;Februar;...\n", Ct);
            Assert.Equal(3, import.Neu);
            Assert.Empty(import.Fehler);
        }

        var vertrag = (await MitAsync(v => v.LadenAsync(vertragId, Ct)))!;
        var vorschlag = vertrag.Mietmodell.Index!;
        Assert.Equal(new DateOnly(2027, 1, 1), vorschlag.VpiMonat);
        Assert.Equal(673.81m, vorschlag.NeueKaltmiete);
        Assert.Equal(3.7m, vorschlag.VeraenderungProzent);
        Assert.Equal(new DateOnly(2027, 3, 1), vorschlag.FruehestensWirksam);
        Assert.False(vorschlag.JetztMoeglich);

        // Erklärung zu früh: wirksam ab 01.02.2027 < 01.03.2027
        Assert.False((await MitAsync(v => v.IndexAnpassenAsync(vertragId, new IndexAnpassungEingabe { ErklaerungAm = new DateOnly(2026, 12, 10) }, Ct))).Erfolgreich);

        umgebung.Uhr.SetUtcNow(new DateTimeOffset(2027, 1, 10, 10, 0, 0, TimeSpan.Zero));
        Assert.Contains(await AufgabenAsync(), a => a.Titel == "Indexanpassung möglich: Wohnung 1 (+3,7 %)");
        Pruefen(await MitAsync(v => v.IndexAnpassenAsync(vertragId, new IndexAnpassungEingabe { ErklaerungAm = new DateOnly(2027, 1, 10) }, Ct)));

        umgebung.Uhr.SetUtcNow(new DateTimeOffset(2027, 3, 2, 10, 0, 0, TimeSpan.Zero));
        vertrag = (await MitAsync(v => v.LadenAsync(vertragId, Ct)))!;
        Assert.Equal(673.81m, vertrag.Mietmodell.Kaltmiete);
        Assert.Equal(121.7m, vertrag.Mietmodell.IndexBasisWert);
        Assert.Null(vertrag.Mietmodell.Index); // kein neuerer VPI als der neue Ausgangsindex
    }

    [Fact]
    public async Task Vpi_manuell_pflegen_und_erneut_importieren()
    {
        await using var scope = umgebung.Services.CreateAsyncScope();
        var vpi = scope.ServiceProvider.GetRequiredService<IVpiVerwaltung>();

        Pruefen(await vpi.SetzenAsync(new DateOnly(2026, 3, 15), 118.2m, Ct));
        Pruefen(await vpi.SetzenAsync(new DateOnly(2026, 3, 1), 118.3m, Ct)); // korrigiert denselben Monat
        var import = await vpi.ImportierenAsync("2026-03;118,3\n03.2026;118,3\n2026-04;118,8\nKaputt;abc\n", Ct);

        var liste = await vpi.ListeAsync(Ct);
        Assert.Equal([new DateOnly(2026, 4, 1), new DateOnly(2026, 3, 1)], liste.Select(v => v.Monat));
        Assert.Equal(118.3m, liste[1].Wert);
        Assert.Equal((1, 0), (import.Neu, import.Geaendert));
    }

    [Theory]
    [InlineData("2026;Januar;117,4", 2026, 1, 117.4)]
    [InlineData("\"2026\";\"März\";\"118,1\"", 2026, 3, 118.1)]
    [InlineData("2026-12;123.0", 2026, 12, 123.0)]
    [InlineData("01.2027\t124,5", 2027, 1, 124.5)]
    public void Vpi_Csv_Formate(string zeile, int jahr, int monat, double wert)
    {
        var (werte, fehler) = VpiVerwaltung.Lesen(zeile);

        Assert.Empty(fehler);
        Assert.Equal((new DateOnly(jahr, monat, 1), (decimal)wert), Assert.Single(werte));
    }

    private async Task<int> AnwendenAsync()
    {
        await using var scope = umgebung.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<StaffelAnwendung>().AnwendenAsync(Ct);
    }

    private async Task<List<Aufgabe>> AufgabenAsync()
    {
        await using var scope = umgebung.Services.CreateAsyncScope();
        var quelle = scope.ServiceProvider.GetServices<IAufgabenQuelle>().OfType<MietmodellAufgaben>().Single();
        return [.. await quelle.AufgabenAsync(Admin, Ct)];
    }

    private static Guid Pruefen(Ergebnis ergebnis)
    {
        Assert.True(ergebnis.Erfolgreich, string.Join(", ", ergebnis.Fehler));
        return ergebnis.Id!.Value;
    }

    private async Task<T> MitAsync<T>(Func<IVertragsVerwaltung, Task<T>> aktion)
    {
        await using var scope = umgebung.Services.CreateAsyncScope();
        return await aktion(scope.ServiceProvider.GetRequiredService<IVertragsVerwaltung>());
    }
}
