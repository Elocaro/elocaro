using Microsoft.EntityFrameworkCore;
using MietHahn.Domain.Gemeinsam;
using MietHahn.Infrastructure.Persistenz;

namespace MietHahn.Integration.Tests.Persistenz;

/// <summary>Testentität, um die gemeinsamen Konventionen ohne Fachentitäten zu prüfen.</summary>
public sealed class TestEintrag : Entitaet
{
    public required string Name { get; set; }

    public decimal Betrag { get; set; }

    public Guid GruppeId { get; set; }

    public Zeitraum Gueltigkeit { get; set; } = new(new DateOnly(2026, 1, 1));

    [Vertraulich]
    public string? Iban { get; set; }

    public List<string> Schlagworte { get; set; } = [];
}

/// <summary>Erweitert das Produktivmodell um <see cref="TestEintrag"/>; Schema per EnsureCreated.</summary>
internal sealed class TestDbContext(DbContextOptions<MietHahnDbContext> options) : MietHahnDbContext(options)
{
    public DbSet<TestEintrag> Eintraege => Set<TestEintrag>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<TestEintrag>(e =>
        {
            e.ToTable("test_eintraege");
            e.Property(x => x.Name).HasMaxLength(100).UseCollation(DeutscheKollation);
        });

        base.OnModelCreating(builder);
    }
}
