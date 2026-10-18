using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MietHahn.Application.Aufgaben;
using MietHahn.Application.Betrieb;
using MietHahn.Application.Konten;
using MietHahn.Domain.Kommunikation;
using MietHahn.Infrastructure.Betrieb;
using MietHahn.Infrastructure.Persistenz;
using MietHahn.Integration.Tests.Infrastruktur;

namespace MietHahn.Integration.Tests.Betrieb;

public class BackupUeberwachungTests(PostgresFixture postgres) : IAsyncLifetime
{
    private InfrastrukturUmgebung umgebung = null!;
    private string statusDatei = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        statusDatei = Path.Combine(Path.GetTempPath(), "miethahn-tests", Guid.NewGuid().ToString("N"), "backup.json");
        Directory.CreateDirectory(Path.GetDirectoryName(statusDatei)!);
        umgebung = await InfrastrukturUmgebung.ErstellenAsync(postgres, new Dictionary<string, string?>
        {
            ["Backup:StatusDatei"] = statusDatei,
            ["Anwendung:BasisUrl"] = "https://miethahn.example.de/",
        });
    }

    public async ValueTask DisposeAsync()
    {
        await umgebung.DisposeAsync();
        Directory.Delete(Path.GetDirectoryName(statusDatei)!, recursive: true);
    }

    [Fact]
    public async Task Ohne_Statusdatei_ist_noch_keine_Sicherung_vorhanden()
    {
        var zustand = await ZustandAsync();

        Assert.Equal(BackupLage.Unbekannt, zustand.Lage);
        Assert.True(zustand.BrauchtAufmerksamkeit);
    }

    [Fact]
    public async Task Erfolgreiche_aktuelle_Sicherung_ist_ok()
    {
        // Format wie von deploy/backup/miethahn-backup.sh geschrieben
        await File.WriteAllTextAsync(statusDatei,
            """{"zeitpunkt":"2026-03-01T01:30:05Z","erfolgreich":true,"meldung":"Sicherung erfolgreich","snapshot":"49ccbcd74357a447","dauerSekunden":5}""", Ct);

        var zustand = await ZustandAsync();

        Assert.Equal(BackupLage.Ok, zustand.Lage);
        Assert.Equal("Letzte Sicherung am 01.03.2026 02:30 erfolgreich.", zustand.Beschreibung);
        Assert.Equal("49ccbcd74357a447", zustand.Snapshot);
        Assert.Equal(5, zustand.DauerSekunden);
        Assert.False(zustand.BrauchtAufmerksamkeit);
    }

    [Fact]
    public async Task Zu_alte_Sicherung_ist_veraltet()
    {
        await StatusSchreibenAsync(erfolgreich: true, InfrastrukturUmgebung.Startzeit.AddHours(-27));

        Assert.Equal(BackupLage.Veraltet, (await ZustandAsync()).Lage);
    }

    [Fact]
    public async Task Fehlgeschlagene_Sicherung_wird_mit_Meldung_gemeldet()
    {
        await StatusSchreibenAsync(erfolgreich: false, InfrastrukturUmgebung.Startzeit.AddHours(-7), "Fatal: unable to open repository");

        var zustand = await ZustandAsync();

        Assert.Equal(BackupLage.Fehlgeschlagen, zustand.Lage);
        Assert.Equal("Fatal: unable to open repository", zustand.Meldung);
    }

    [Fact]
    public async Task Unlesbare_Statusdatei_ist_unbekannt()
    {
        await File.WriteAllTextAsync(statusDatei, "{kaputt", Ct);

        Assert.Equal(BackupLage.Unbekannt, (await ZustandAsync()).Lage);
    }

    [Fact]
    public async Task Ohne_Konfiguration_ist_die_Ueberwachung_aus()
    {
        await using var ohne = await InfrastrukturUmgebung.ErstellenAsync(postgres);
        await using var scope = ohne.Services.CreateAsyncScope();

        var zustand = await scope.ServiceProvider.GetRequiredService<IBackupStatus>().AbfragenAsync(Ct);

        Assert.Equal(BackupLage.NichtEingerichtet, zustand.Lage);
        Assert.False(zustand.BrauchtAufmerksamkeit);
    }

    [Fact]
    public async Task Health_Check_meldet_Probleme_als_Degraded()
    {
        await StatusSchreibenAsync(erfolgreich: false, InfrastrukturUmgebung.Startzeit);
        await using var scope = umgebung.Services.CreateAsyncScope();
        var bericht = await scope.ServiceProvider.GetRequiredService<HealthCheckService>().CheckHealthAsync(c => c.Name == "backup", Ct);

        Assert.Equal(HealthStatus.Degraded, bericht.Entries["backup"].Status);
    }

    [Fact]
    public async Task Dashboard_zeigt_Problem_nur_mit_Systemrecht()
    {
        await StatusSchreibenAsync(erfolgreich: false, InfrastrukturUmgebung.Startzeit);
        await using var scope = umgebung.Services.CreateAsyncScope();
        var quelle = scope.ServiceProvider.GetServices<IAufgabenQuelle>().OfType<BackupAufgaben>().Single();

        var admin = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, Rollen.Admin)], "test"));
        var verwalter = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, Rollen.Verwalter), new Claim(Rechte.ClaimTyp, nameof(Recht.Finanzen))], "test"));

        var aufgabe = Assert.Single(await quelle.AufgabenAsync(admin, Ct));
        Assert.Equal(Dringlichkeit.Dringend, aufgabe.Dringlichkeit);
        Assert.Equal("/verwaltung/system/datensicherung", aufgabe.Link);
        Assert.Empty(await quelle.AufgabenAsync(verwalter, Ct));
    }

    [Fact]
    public async Task Warnung_geht_an_alle_aktiven_Admins()
    {
        await using (var scope = umgebung.Services.CreateAsyncScope())
        {
            var konten = scope.ServiceProvider.GetRequiredService<IKontoverwaltung>();
            await konten.ErstenAdminAnlegenAsync("admin@example.de", "Admin", "Langes-Passwort-2026", Ct);
            var zweiter = await konten.EinladenAsync("admin2@example.de", "Zweiter Admin", Rollen.Admin, Ct);
            var gesperrt = await konten.EinladenAsync("gesperrt@example.de", "Gesperrt", Rollen.Admin, Ct);
            await konten.EinladenAsync("verwalter@example.de", "Verwalter", Rollen.Verwalter, Ct);
            Assert.True(zweiter.Erfolgreich && gesperrt.Erfolgreich);
            await scope.ServiceProvider.GetRequiredService<IBenutzerverwaltung>().SperrenAsync(gesperrt.BenutzerId!.Value, Ct);
        }

        await StatusSchreibenAsync(erfolgreich: false, InfrastrukturUmgebung.Startzeit.AddHours(-7), "Fatal: unable to open repository");

        Assert.Equal(2, await WarnenAsync());

        var mail = Assert.Single(await WarnmailsAsync());
        Assert.Equal(["admin2@example.de", "admin@example.de"], mail.An.Order(StringComparer.Ordinal));
        Assert.Equal("MietHahn: Datensicherung prüfen", mail.Betreff);
        Assert.Contains("fehlgeschlagen", mail.Html, StringComparison.Ordinal);
        Assert.Contains("Fatal: unable to open repository", mail.Html, StringComparison.Ordinal);
        Assert.Contains("https://miethahn.example.de/verwaltung/system/datensicherung", mail.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ohne_Problem_wird_nicht_gewarnt()
    {
        await using (var scope = umgebung.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IKontoverwaltung>().ErstenAdminAnlegenAsync("admin@example.de", "Admin", "Langes-Passwort-2026", Ct);
        }

        await StatusSchreibenAsync(erfolgreich: true, InfrastrukturUmgebung.Startzeit.AddHours(-7));

        Assert.Equal(0, await WarnenAsync());
        Assert.Empty(await WarnmailsAsync());
    }

    private async Task StatusSchreibenAsync(bool erfolgreich, DateTimeOffset zeitpunkt, string meldung = "") =>
        await File.WriteAllTextAsync(statusDatei,
            $$"""{"zeitpunkt":"{{zeitpunkt.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}}","erfolgreich":{{(erfolgreich ? "true" : "false")}},"meldung":"{{meldung}}","snapshot":"","dauerSekunden":3}""", Ct);

    private async Task<BackupZustand> ZustandAsync()
    {
        await using var scope = umgebung.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IBackupStatus>().AbfragenAsync(Ct);
    }

    private async Task<int> WarnenAsync()
    {
        await using var scope = umgebung.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<BackupWarnung>().PruefenAsync(Ct);
    }

    private async Task<List<AusgehendeMail>> WarnmailsAsync()
    {
        await using var scope = umgebung.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<MietHahnDbContext>().Set<AusgehendeMail>().Where(m => m.Vorlage == "backup-warnung").ToListAsync(Ct);
    }
}
