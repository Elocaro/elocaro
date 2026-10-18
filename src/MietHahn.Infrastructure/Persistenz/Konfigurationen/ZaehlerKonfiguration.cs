using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MietHahn.Domain.Dateien;
using MietHahn.Domain.Objekte;
using MietHahn.Domain.Zaehlerwesen;

namespace MietHahn.Infrastructure.Persistenz.Konfigurationen;

internal sealed class ZaehlerKonfiguration : IEntityTypeConfiguration<Zaehler>
{
    public void Configure(EntityTypeBuilder<Zaehler> builder)
    {
        builder.ToTable("zaehler");
        builder.Property(z => z.Typ).HasConversion<string>().HasMaxLength(20);
        builder.Property(z => z.Messeinheit).HasConversion<string>().HasMaxLength(20);
        builder.Property(z => z.Bezeichnung).HasMaxLength(200).UseCollation(MietHahnDbContext.DeutscheKollation);
        builder.Property(z => z.Nummer).HasMaxLength(50);
        builder.Property(z => z.Lage).HasMaxLength(200);
        builder.Property(z => z.Notizen).HasMaxLength(2000);
        builder.HasIndex(z => z.Nummer);

        builder.HasOne<Objekt>().WithMany().HasForeignKey(z => z.ObjektId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Einheit>().WithMany().HasForeignKey(z => z.EinheitId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Zimmer>().WithMany().HasForeignKey(z => z.ZimmerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Zaehler>().WithMany().HasForeignKey(z => z.HauptzaehlerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Zaehler>().WithMany().HasForeignKey(z => z.VorgaengerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(z => z.Staende).WithOne().HasForeignKey(s => s.ZaehlerId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ZaehlerstandKonfiguration : IEntityTypeConfiguration<Zaehlerstand>
{
    public void Configure(EntityTypeBuilder<Zaehlerstand> builder)
    {
        builder.ToTable("zaehlerstaende");
        builder.Property(s => s.Wert).HasPrecision(14, 3);
        builder.Property(s => s.Art).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.Ableser).HasMaxLength(256);
        builder.Property(s => s.Notiz).HasMaxLength(1000);
        builder.HasOne<Datei>().WithMany().HasForeignKey(s => s.FotoDateiId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(s => new { s.ZaehlerId, s.Datum }).IsUnique().HasFilter("NOT geloescht");
    }
}
