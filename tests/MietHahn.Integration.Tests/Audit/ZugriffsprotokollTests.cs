using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Audit;
using MietHahn.Infrastructure.Audit;
using MietHahn.Infrastructure.Persistenz;
using MietHahn.Integration.Tests.Infrastruktur;

namespace MietHahn.Integration.Tests.Audit;

public class ZugriffsprotokollTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Lesender_Zugriff_erscheint_mit_Zweck_in_der_Historie()
    {
        await using var umgebung = await InfrastrukturUmgebung.ErstellenAsync(postgres);
        var mieterId = Guid.CreateVersion7();

        await using (var scope = umgebung.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IZugriffsprotokoll>().ProtokollierenAsync("Person", mieterId, "Mieterakte geöffnet", Ct);
        }

        await using var lesen = umgebung.Services.CreateAsyncScope();
        var eintrag = Assert.Single(await lesen.ServiceProvider.GetRequiredService<IAenderungsHistorie>().LadenAsync("Person", mieterId, Ct));
        Assert.Equal(AuditAktion.Gelesen, eintrag.Aktion);
        Assert.Equal("Mieterakte geöffnet", eintrag.Zweck);
        Assert.Equal("system", eintrag.Benutzer);
    }

    [Fact]
    public async Task Wiederholter_Zugriff_wird_nur_einmal_je_Zeitfenster_protokolliert()
    {
        await using var umgebung = await InfrastrukturUmgebung.ErstellenAsync(postgres);
        var mieterId = Guid.CreateVersion7();

        async Task OeffnenAsync(string zweck = "Mieterakte geöffnet")
        {
            await using var scope = umgebung.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IZugriffsprotokoll>().ProtokollierenAsync("Person", mieterId, zweck, Ct);
        }

        await OeffnenAsync();
        await OeffnenAsync();
        umgebung.Uhr.Advance(Zugriffsprotokoll.Zeitfenster - TimeSpan.FromMinutes(1));
        await OeffnenAsync();
        await OeffnenAsync("DSGVO-Auskunft"); // anderer Zweck zählt eigens
        umgebung.Uhr.Advance(TimeSpan.FromMinutes(2));
        await OeffnenAsync();

        await using var lesen = umgebung.Services.CreateAsyncScope();
        var eintraege = await lesen.ServiceProvider.GetRequiredService<IAenderungsHistorie>().LadenAsync("Person", mieterId, Ct);
        Assert.Equal(3, eintraege.Count);
    }

    [Fact]
    public async Task Zugriffsprotokoll_ist_abschaltbar()
    {
        await using var umgebung = await InfrastrukturUmgebung.ErstellenAsync(postgres, new Dictionary<string, string?> { ["Audit:ZugriffeProtokollieren"] = "false" });

        await using (var scope = umgebung.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IZugriffsprotokoll>().ProtokollierenAsync("Person", Guid.CreateVersion7(), "Test", Ct);
        }

        await using var pruefung = umgebung.Services.CreateAsyncScope();
        Assert.False(await pruefung.ServiceProvider.GetRequiredService<MietHahnDbContext>().Set<AuditEintrag>().AnyAsync(Ct));
    }

    [Fact]
    public async Task Bereinigung_beachtet_getrennte_Fristen()
    {
        await using var umgebung = await InfrastrukturUmgebung.ErstellenAsync(postgres, new Dictionary<string, string?>
        {
            ["Audit:AufbewahrungJahre"] = "10",
            ["Audit:ZugriffeAufbewahrungMonate"] = "12",
        });
        var jetzt = InfrastrukturUmgebung.Startzeit;

        await using (var scope = umgebung.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MietHahnDbContext>();
            db.AddRange(
                new AuditEintrag(jetzt.AddYears(-11), "x", "A", Guid.CreateVersion7(), AuditAktion.Geaendert, "[]"),
                new AuditEintrag(jetzt.AddYears(-9), "x", "B", Guid.CreateVersion7(), AuditAktion.Geaendert, "[]"),
                new AuditEintrag(jetzt.AddMonths(-13), "x", "C", Guid.CreateVersion7(), AuditAktion.Gelesen, "[]", "alt"),
                new AuditEintrag(jetzt.AddMonths(-11), "x", "D", Guid.CreateVersion7(), AuditAktion.Gelesen, "[]", "neu"));
            await db.SaveChangesAsync(Ct);
        }

        await using (var scope = umgebung.Services.CreateAsyncScope())
        {
            Assert.Equal(2, await scope.ServiceProvider.GetRequiredService<AuditBereinigung>().BereinigenAsync(Ct));
        }

        await using var pruefung = umgebung.Services.CreateAsyncScope();
        var uebrig = await pruefung.ServiceProvider.GetRequiredService<MietHahnDbContext>().Set<AuditEintrag>()
            .Select(a => a.EntitaetTyp).OrderBy(t => t).ToListAsync(Ct);
        Assert.Equal(["B", "D"], uebrig);
    }
}
