using Elocaro.Domain.Common;
using Elocaro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Elocaro.Integration.Tests.Persistence;

/// <summary>Test entity for checking the shared conventions without domain entities.</summary>
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

/// <summary>Extends the production model with <see cref="TestEntry"/>; schema via EnsureCreated.</summary>
internal sealed class TestDbContext(DbContextOptions<ElocaroDbContext> options) : ElocaroDbContext(options)
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
