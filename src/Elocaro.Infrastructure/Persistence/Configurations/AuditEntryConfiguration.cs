using Elocaro.Infrastructure.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elocaro.Infrastructure.Persistence.Configurations;

internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("audit_entries");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.User).HasMaxLength(256);
        builder.Property(a => a.EntityType).HasMaxLength(100);
        builder.Property(a => a.Action).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.ChangesJson).HasColumnName("changes").HasColumnType("jsonb");
        builder.Property(a => a.Purpose).HasMaxLength(200);
        builder.Property(a => a.RestoreJson).HasColumnName("restore").HasColumnType("jsonb");
        builder.HasIndex(a => new { a.EntityType, a.EntityId, a.Timestamp });
        builder.HasIndex(a => a.Timestamp);
        builder.HasIndex(a => a.OperationId);
    }
}
