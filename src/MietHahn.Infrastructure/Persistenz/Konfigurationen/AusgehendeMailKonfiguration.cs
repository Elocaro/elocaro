using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MietHahn.Domain.Kommunikation;

namespace MietHahn.Infrastructure.Persistenz.Konfigurationen;

internal sealed class AusgehendeMailKonfiguration : IEntityTypeConfiguration<AusgehendeMail>
{
    public void Configure(EntityTypeBuilder<AusgehendeMail> builder)
    {
        builder.ToTable("ausgehende_mails");
        builder.Property(m => m.Betreff).HasMaxLength(500);
        builder.Property(m => m.Vorlage).HasMaxLength(100);
        builder.Property(m => m.Bezug).HasMaxLength(200);
        builder.Property(m => m.LetzterFehler).HasMaxLength(AusgehendeMail.MaxFehlerLaenge);
        builder.Property(m => m.Status).HasConversion<string>().HasMaxLength(20);

        // Für den Outbox-Job: nur ausstehende Mails, sortiert nach Fälligkeit.
        builder.HasIndex(m => m.NaechsterVersuchAm).HasFilter("status = 'Ausstehend'");
        builder.HasIndex(m => m.Bezug);
    }
}
