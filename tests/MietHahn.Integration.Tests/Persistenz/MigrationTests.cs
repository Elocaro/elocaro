using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MietHahn.Infrastructure;
using MietHahn.Infrastructure.Persistenz;
using MietHahn.Integration.Tests.Infrastruktur;

namespace MietHahn.Integration.Tests.Persistenz;

public class MigrationTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Migration_legt_Schema_in_leerer_Datenbank_an()
    {
        await using var services = await ServicesAsync(automatischMigrieren: true);

        await services.MigriereDatenbankAsync(Ct);

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MietHahnDbContext>();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync(Ct));
        Assert.True(await ExtensionVorhandenAsync(db, "btree_gist"));
    }

    [Fact]
    public async Task Ohne_AutomatischMigrieren_bleibt_die_Datenbank_leer()
    {
        await using var services = await ServicesAsync(automatischMigrieren: false);

        await services.MigriereDatenbankAsync(Ct);

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MietHahnDbContext>();
        Assert.NotEmpty(await db.Database.GetPendingMigrationsAsync(Ct));
    }

    [Fact]
    public async Task Modell_und_Migrationen_sind_synchron()
    {
        await using var services = await ServicesAsync(automatischMigrieren: true);
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MietHahnDbContext>();

        Assert.False(db.Database.HasPendingModelChanges(), "Modelländerungen ohne Migration: 'dotnet ef migrations add <Name>' ausführen.");
    }

    [Fact]
    public async Task Fehlende_Verbindungszeichenfolge_wird_klar_gemeldet()
    {
        var konfiguration = new ConfigurationBuilder().Build();
        await using var services = new ServiceCollection()
            .AddLogging()
            .AddMietHahnInfrastruktur(konfiguration)
            .BuildServiceProvider();

        await using var scope = services.CreateAsyncScope();
        var fehler = Assert.Throws<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<MietHahnDbContext>());
        Assert.Contains("ConnectionStrings:MietHahn", fehler.Message, StringComparison.Ordinal);
    }

    private async Task<ServiceProvider> ServicesAsync(bool automatischMigrieren)
    {
        var verbindung = await postgres.NeueDatenbankAsync(Ct);
        var konfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:MietHahn"] = verbindung,
                ["Datenbank:AutomatischMigrieren"] = automatischMigrieren.ToString(),
            })
            .Build();

        return new ServiceCollection()
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddMietHahnInfrastruktur(konfiguration)
            .BuildServiceProvider();
    }

    private static async Task<bool> ExtensionVorhandenAsync(MietHahnDbContext db, string name) =>
        await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM pg_extension WHERE extname = {name}").SingleAsync(Ct) == 1;
}
