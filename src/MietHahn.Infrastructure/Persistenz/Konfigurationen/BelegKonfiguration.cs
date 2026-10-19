using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MietHahn.Domain.Belege;
using MietHahn.Domain.Dateien;
using MietHahn.Domain.Objekte;
using MietHahn.Domain.Personen;

namespace MietHahn.Infrastructure.Persistenz.Konfigurationen;

internal sealed class BelegKonfiguration : IEntityTypeConfiguration<Beleg>
{
    public void Configure(EntityTypeBuilder<Beleg> builder)
    {
        builder.ToTable("belege");
        builder.Property(b => b.LieferantName).HasMaxLength(200).UseCollation(MietHahnDbContext.DeutscheKollation);
        builder.Property(b => b.Rechnungsnummer).HasMaxLength(100);
        builder.Property(b => b.Brennstoffeinheit).HasMaxLength(10);
        builder.Property(b => b.Brennstoffmenge).HasPrecision(14, 3);
        builder.Property(b => b.Co2Kg).HasPrecision(14, 3);
        builder.Property(b => b.Notiz).HasMaxLength(2000);
        builder.Ignore(b => b.Leistungszeitraum);
        builder.Ignore(b => b.Aufgeteilt);
        builder.Ignore(b => b.IstVollstaendigAufgeteilt);
        builder.HasOne<Person>().WithMany().HasForeignKey(b => b.LieferantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(b => b.Dateien).WithOne().HasForeignKey(d => d.BelegId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(b => b.Positionen).WithOne().HasForeignKey(p => p.BelegId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(b => b.Rechnungsdatum);
    }
}

internal sealed class BelegDateiKonfiguration : IEntityTypeConfiguration<BelegDatei>
{
    public void Configure(EntityTypeBuilder<BelegDatei> builder)
    {
        builder.ToTable("beleg_dateien");
        builder.HasOne<Datei>().WithMany().HasForeignKey(d => d.DateiId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class KostenpositionKonfiguration : IEntityTypeConfiguration<Kostenposition>
{
    public void Configure(EntityTypeBuilder<Kostenposition> builder)
    {
        builder.ToTable("kostenpositionen");
        builder.Property(p => p.Kostenart).HasMaxLength(40);
        builder.Property(p => p.Paragraf35a).HasConversion<string>().HasMaxLength(40);
        builder.Property(p => p.Notiz).HasMaxLength(500);
        builder.HasOne<Objekt>().WithMany().HasForeignKey(p => p.ObjektId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Einheit>().WithMany().HasForeignKey(p => p.EinheitId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(p => new { p.ObjektId, p.Kostenart });
    }
}
