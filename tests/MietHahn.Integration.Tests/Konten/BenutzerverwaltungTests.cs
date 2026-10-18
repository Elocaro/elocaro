using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Konten;
using MietHahn.Infrastructure.Identitaet;
using MietHahn.Infrastructure.Persistenz;
using MietHahn.Integration.Tests.Infrastruktur;

namespace MietHahn.Integration.Tests.Konten;

public class BenutzerverwaltungTests(PostgresFixture postgres) : IAsyncLifetime
{
    private InfrastrukturUmgebung umgebung = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => umgebung = await InfrastrukturUmgebung.ErstellenAsync(postgres);

    public async ValueTask DisposeAsync() => await umgebung.DisposeAsync();

    [Fact]
    public async Task Eingeladener_Verwalter_erhaelt_Standardrechte()
    {
        var id = await EinladenAsync("v@example.de", Rollen.Verwalter);

        var benutzer = await MitAsync(v => v.LadenAsync(id, Ct));

        Assert.Equal(Rollen.Verwalter, benutzer!.Rolle);
        Assert.Equal(Rechte.VerwalterStandard, benutzer.Rechte);
        Assert.False(benutzer.EinladungAngenommen);
        Assert.False(benutzer.HatPasswort);
    }

    [Fact]
    public async Task Admins_haben_immer_alle_Rechte()
    {
        await MitKontenAsync(k => k.ErstenAdminAnlegenAsync("admin@example.de", "Admin", "Langes-Passwort-2026", Ct));

        var admin = (await MitAsync(v => v.ListeAsync(Ct))).Single();

        Assert.Equal(Rechte.Alle, admin.Rechte);
    }

    [Fact]
    public async Task Rechte_gibt_es_nur_fuer_Verwalter()
    {
        var id = await EinladenAsync("m@example.de", Rollen.Mieter);

        var ergebnis = await MitAsync(v => v.RechteSetzenAsync(id, [Recht.Finanzen], Ct));

        Assert.False(ergebnis.Erfolgreich);
    }

    [Fact]
    public async Task Rollenwechsel_setzt_die_Rechte_passend()
    {
        var id = await EinladenAsync("x@example.de", Rollen.Verwalter);
        await MitAsync(v => v.RechteSetzenAsync(id, [Recht.System], Ct));

        await MitAsync(v => v.RolleAendernAsync(id, Rollen.Hausmeister, Ct));
        Assert.Empty((await MitAsync(v => v.LadenAsync(id, Ct)))!.Rechte);

        await MitAsync(v => v.RolleAendernAsync(id, Rollen.Verwalter, Ct));
        Assert.Equal(Rechte.VerwalterStandard, (await MitAsync(v => v.LadenAsync(id, Ct)))!.Rechte);
    }

    [Fact]
    public async Task Sperren_entwertet_offene_Anmeldelinks()
    {
        var id = await EinladenAsync("mieter@example.de", Rollen.Mieter);

        await MitAsync(v => v.SperrenAsync(id, Ct));

        await using var scope = umgebung.Services.CreateAsyncScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<MietHahnDbContext>().Set<Anmeldelink>().AnyAsync(l => l.BenutzerId == id, Ct));
    }

    [Fact]
    public async Task Unbekannte_Rolle_wird_abgelehnt()
    {
        var id = await EinladenAsync("y@example.de", Rollen.Mieter);

        Assert.False((await MitAsync(v => v.RolleAendernAsync(id, "Superuser", Ct))).Erfolgreich);
        Assert.False((await MitKontenAsync(k => k.EinladenAsync("z@example.de", "Z", "Superuser", Ct))).Erfolgreich);
    }

    private async Task<Guid> EinladenAsync(string email, string rolle)
    {
        var ergebnis = await MitKontenAsync(k => k.EinladenAsync(email, "Test", rolle, Ct));
        Assert.True(ergebnis.Erfolgreich, string.Join(", ", ergebnis.Fehler));
        return ergebnis.BenutzerId!.Value;
    }

    private async Task<T> MitAsync<T>(Func<IBenutzerverwaltung, Task<T>> aktion)
    {
        await using var scope = umgebung.Services.CreateAsyncScope();
        return await aktion(scope.ServiceProvider.GetRequiredService<IBenutzerverwaltung>());
    }

    private async Task<T> MitKontenAsync<T>(Func<IKontoverwaltung, Task<T>> aktion)
    {
        await using var scope = umgebung.Services.CreateAsyncScope();
        return await aktion(scope.ServiceProvider.GetRequiredService<IKontoverwaltung>());
    }
}
