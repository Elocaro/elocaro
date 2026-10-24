using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MietHahn.Domain.Admin;

namespace MietHahn.Infrastructure.Persistence.Configurations;

internal sealed class LegalReferenceCheckConfiguration : IEntityTypeConfiguration<LegalReferenceCheck>
{
    public void Configure(EntityTypeBuilder<LegalReferenceCheck> builder)
    {
        builder.ToTable("legal_reference_checks");
        builder.Property(c => c.Key).HasMaxLength(40);
        builder.Property(c => c.Fingerprint).HasMaxLength(200);
        builder.Property(c => c.CheckedBy).HasMaxLength(200);
        builder.Property(c => c.Source).HasMaxLength(500);
        builder.HasIndex(c => c.Key).IsUnique();
    }
}
