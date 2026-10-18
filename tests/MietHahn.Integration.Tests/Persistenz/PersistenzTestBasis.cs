using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using MietHahn.Infrastructure;
using MietHahn.Infrastructure.Persistenz;
using MietHahn.Integration.Tests.Infrastruktur;

namespace MietHahn.Integration.Tests.Persistenz;

/// <summary>Stellt je Test eine eigene Datenbank mit dem <see cref="TestDbContext"/>-Schema bereit.</summary>
public abstract class PersistenzTestBasis(PostgresFixture postgres) : IAsyncLifetime
{
    protected static readonly DateTimeOffset Startzeit = new(2026, 3, 1, 9, 30, 0, TimeSpan.Zero);

    private string verbindung = "";

    internal FakeTimeProvider Uhr { get; } = new(Startzeit);

    internal TestBenutzer Benutzer { get; } = new();

    protected string Verbindung => verbindung;

    public virtual async ValueTask InitializeAsync()
    {
        verbindung = await postgres.NeueDatenbankAsync(TestContext.Current.CancellationToken);
        await using var db = NeuerContext();
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    internal TestDbContext NeuerContext()
    {
        var optionen = new DbContextOptionsBuilder<MietHahnDbContext>();
        DependencyInjection.KonfiguriereNpgsql(optionen, verbindung);
        optionen.AddInterceptors(new EntitaetSpeichernInterceptor(Uhr, Benutzer));
        return new TestDbContext(optionen.Options);
    }
}
