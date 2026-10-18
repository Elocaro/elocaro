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

    public DateOnly Von { get; set; }

    public DateOnly? Bis { get; set; }
}

/// <summary>Erweitert das Produktivmodell um <see cref="TestEintrag"/>; Schema per EnsureCreated.</summary>
internal sealed class TestDbContext(DbContextOptions<MietHahnDbContext> options) : MietHahnDbContext(options)
{
    public DbSet<TestEintrag> Eintraege => Set<TestEintrag>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TestEintrag>(e =>
        {
            e.ToTable("test_eintraege");
            e.Property(x => x.Name).HasMaxLength(100).UseCollation(DeutscheKollation);
        });

        base.OnModelCreating(modelBuilder);
    }
}
