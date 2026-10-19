using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MietHahn.Domain.Finanzen;
using MietHahn.Domain.Vertraege;

namespace MietHahn.Infrastructure.Persistenz.Konfigurationen;

internal sealed class SollstellungKonfiguration : IEntityTypeConfiguration<Sollstellung>
{
    public void Configure(EntityTypeBuilder<Sollstellung> builder)
    {
        builder.ToTable("sollstellungen");
        builder.Property(s => s.Art).HasConversion<string>().HasMaxLength(30);
        builder.Property(s => s.Bestandteil).HasConversion<string>().HasMaxLength(40);
        builder.Property(s => s.Bezeichnung).HasMaxLength(200);
        builder.Property(s => s.Notiz).HasMaxLength(1000);
        builder.HasOne<Mietvertrag>().WithMany().HasForeignKey(s => s.MietvertragId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(s => new { s.MietvertragId, s.Faellig });

        // Der Job darf je Vertrag, Monat und Bestandteil nur eine Monatsmiete anlegen (Idempotenz auch bei parallelen Läufen).
        builder.HasIndex(s => new { s.MietvertragId, s.Leistungsmonat, s.Bestandteil }).IsUnique()
            .HasFilter("art = 'Miete' AND NOT geloescht")
            .HasDatabaseName("ix_sollstellungen_monatsmiete_eindeutig");
    }
}

internal sealed class ZahlungKonfiguration : IEntityTypeConfiguration<Zahlung>
{
    public void Configure(EntityTypeBuilder<Zahlung> builder)
    {
        builder.ToTable("zahlungen");
        builder.Property(z => z.Herkunft).HasConversion<string>().HasMaxLength(20);
        builder.Property(z => z.Verwendungszweck).HasMaxLength(500);
        builder.HasOne<Mietvertrag>().WithMany().HasForeignKey(z => z.MietvertragId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Sollstellung>().WithMany().HasForeignKey(z => z.SollstellungId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(z => new { z.MietvertragId, z.Datum });
    }
}

internal sealed class FinanzeinstellungenKonfiguration : IEntityTypeConfiguration<Finanzeinstellungen>
{
    public void Configure(EntityTypeBuilder<Finanzeinstellungen> builder)
    {
        builder.ToTable("finanzeinstellungen");
        builder.Property(f => f.Verrechnung).HasConversion<string>().HasMaxLength(30);
    }
}
