using Microsoft.EntityFrameworkCore;
using MietHahn.Domain.Common;
using MietHahn.Infrastructure.Persistence;

namespace MietHahn.Integration.Tests.Persistence;

/// <summary>Testentität, um die gemeinsamen Konventionen ohne Fachentitäten zu prüfen.</summary>
public sealed class TestEntry : Entity
{
    public required string Name { get; set; }

    public decimal Amount { get; set; }

    public Guid GroupId { get; set; }

    public Period Validity { get; set; } = new(new DateOnly(2026, 1, 1));

    [Confidential]
    public string? Iban { get; set; }

    public List<string> Tags { get; set; } = [];
}

/// <summary>Erweitert das Produktivmodell um <see cref="TestEntry"/>; Schema per EnsureCreated.</summary>
internal sealed class TestDbContext(DbContextOptions<MietHahnDbContext> options) : MietHahnDbContext(options)
{
    public DbSet<TestEntry> Entries => Set<TestEntry>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<TestEntry>(e =>
        {
            e.ToTable("test_entries");
            e.Property(x => x.Name).HasMaxLength(100).UseCollation(GermanCollation);
        });

        base.OnModelCreating(builder);
    }
}
