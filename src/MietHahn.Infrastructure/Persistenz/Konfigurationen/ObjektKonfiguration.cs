using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MietHahn.Domain.Dateien;
using MietHahn.Domain.Objekte;
using MietHahn.Infrastructure.Identitaet;

namespace MietHahn.Infrastructure.Persistenz.Konfigurationen;

internal sealed class ObjektKonfiguration : IEntityTypeConfiguration<Objekt>
{
    public void Configure(EntityTypeBuilder<Objekt> builder)
    {
        builder.ToTable("objekte");
        builder.Property(o => o.Bezeichnung).HasMaxLength(200).UseCollation(MietHahnDbContext.DeutscheKollation);
        builder.Property(o => o.Strasse).HasMaxLength(200);
        builder.Property(o => o.Plz).HasMaxLength(10);
        builder.Property(o => o.Ort).HasMaxLength(100);
        builder.Property(o => o.Gemarkung).HasMaxLength(100);
        builder.Property(o => o.Flurstueck).HasMaxLength(50);
        builder.Property(o => o.GrundsteuerAktenzeichen).HasMaxLength(50);
        builder.Property(o => o.Notizen).HasMaxLength(4000);
        builder.Property(o => o.Heizungsart).HasConversion<string>().HasMaxLength(30);
        builder.Property(o => o.Energietraeger).HasConversion<string>().HasMaxLength(30);
        builder.HasOne<Datei>().WithMany().HasForeignKey(o => o.BildDateiId).OnDelete(DeleteBehavior.Restrict);
        builder.Ignore(o => o.Anschrift);

        // Kinder des Aggregats: Entfernen aus der Liste löscht (logisch, siehe EntitaetSpeichernInterceptor).
        builder.HasMany(o => o.Einheiten).WithOne().HasForeignKey(e => e.ObjektId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(o => o.Flaechen).WithOne().HasForeignKey(f => f.ObjektId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class EinheitKonfiguration : IEntityTypeConfiguration<Einheit>
{
    public void Configure(EntityTypeBuilder<Einheit> builder)
    {
        builder.ToTable("einheiten");
        builder.Property(e => e.Bezeichnung).HasMaxLength(100).UseCollation(MietHahnDbContext.DeutscheKollation);
        builder.Property(e => e.Lage).HasMaxLength(100);
        builder.Property(e => e.Typ).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Zimmeranzahl).HasPrecision(4, 1);
        builder.Property(e => e.Bad).HasMaxLength(100);
        builder.Property(e => e.Energiestandard).HasMaxLength(50);
        builder.Property(e => e.Ausstattung).HasMaxLength(2000);
        builder.Ignore(e => e.IstWg);
        builder.Ignore(e => e.IstSelbstVermietbar);

        builder.HasMany(e => e.Zimmer).WithOne().HasForeignKey(z => z.EinheitId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(e => e.Flaechen).WithOne().HasForeignKey(f => f.EinheitId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ZimmerKonfiguration : IEntityTypeConfiguration<Zimmer>
{
    public void Configure(EntityTypeBuilder<Zimmer> builder)
    {
        builder.ToTable("zimmer");
        builder.Property(z => z.Bezeichnung).HasMaxLength(100).UseCollation(MietHahnDbContext.DeutscheKollation);
        builder.Property(z => z.Moeblierung).HasConversion<string>().HasMaxLength(20);
        builder.Property(z => z.Notizen).HasMaxLength(2000);
        builder.HasMany(z => z.Flaechen).WithOne().HasForeignKey(f => f.ZimmerId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ObjektFlaecheKonfiguration : IEntityTypeConfiguration<ObjektFlaeche>
{
    public void Configure(EntityTypeBuilder<ObjektFlaeche> builder)
    {
        builder.ToTable("objekt_flaechen");
        builder.Property(f => f.Gesamtflaeche).HasPrecision(10, 2);
        builder.Property(f => f.Gemeinschaftsflaeche).HasPrecision(10, 2);
    }
}

internal sealed class EinheitFlaecheKonfiguration : IEntityTypeConfiguration<EinheitFlaeche>
{
    public void Configure(EntityTypeBuilder<EinheitFlaeche> builder)
    {
        builder.ToTable("einheit_flaechen");
        builder.Property(f => f.Wohnflaeche).HasPrecision(10, 2);
        builder.Property(f => f.Heizflaeche).HasPrecision(10, 2);
    }
}

internal sealed class ZimmerFlaecheKonfiguration : IEntityTypeConfiguration<ZimmerFlaeche>
{
    public void Configure(EntityTypeBuilder<ZimmerFlaeche> builder)
    {
        builder.ToTable("zimmer_flaechen");
        builder.Property(f => f.Flaeche).HasPrecision(10, 2);
        builder.Property(f => f.AnteilGemeinschaftsflaeche).HasPrecision(10, 2);
        builder.Ignore(f => f.Umlageflaeche);
    }
}

internal sealed class ObjektZuordnungKonfiguration : IEntityTypeConfiguration<ObjektZuordnung>
{
    public void Configure(EntityTypeBuilder<ObjektZuordnung> builder)
    {
        builder.ToTable("objekt_zuordnungen");
        builder.HasOne<Objekt>().WithMany().HasForeignKey(z => z.ObjektId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Benutzer>().WithMany().HasForeignKey(z => z.BenutzerId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(z => new { z.BenutzerId, z.ObjektId }).IsUnique().HasFilter("NOT geloescht");
    }
}
