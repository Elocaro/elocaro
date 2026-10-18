using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MietHahn.Infrastructure.Audit;

namespace MietHahn.Infrastructure.Persistenz.Konfigurationen;

internal sealed class AuditEintragKonfiguration : IEntityTypeConfiguration<AuditEintrag>
{
    public void Configure(EntityTypeBuilder<AuditEintrag> builder)
    {
        builder.ToTable("audit_eintraege");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Benutzer).HasMaxLength(256);
        builder.Property(a => a.EntitaetTyp).HasMaxLength(100);
        builder.Property(a => a.Aktion).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.AenderungenJson).HasColumnName("aenderungen").HasColumnType("jsonb");
        builder.Property(a => a.Zweck).HasMaxLength(200);
        builder.HasIndex(a => new { a.EntitaetTyp, a.EntitaetId, a.Zeitpunkt });
        builder.HasIndex(a => a.Zeitpunkt);
    }
}
