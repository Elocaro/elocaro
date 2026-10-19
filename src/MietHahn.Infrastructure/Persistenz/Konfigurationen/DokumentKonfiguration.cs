using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MietHahn.Domain.Dateien;
using MietHahn.Domain.Dokumente;
using MietHahn.Domain.Objekte;
using MietHahn.Domain.Personen;
using MietHahn.Domain.Vertraege;

namespace MietHahn.Infrastructure.Persistenz.Konfigurationen;

internal sealed class DokumentKonfiguration : IEntityTypeConfiguration<Dokument>
{
    public void Configure(EntityTypeBuilder<Dokument> builder)
    {
        builder.ToTable("dokumente");
        builder.Property(d => d.Titel).HasMaxLength(200).UseCollation(MietHahnDbContext.DeutscheKollation);
        builder.Property(d => d.Kategorie).HasConversion<string>().HasMaxLength(30);
        builder.Property(d => d.Notiz).HasMaxLength(2000);
        builder.Property(d => d.Tags).HasColumnType("text[]");
        builder.HasOne<Datei>().WithMany().HasForeignKey(d => d.DateiId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Objekt>().WithMany().HasForeignKey(d => d.ObjektId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Einheit>().WithMany().HasForeignKey(d => d.EinheitId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Mietvertrag>().WithMany().HasForeignKey(d => d.MietvertragId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Person>().WithMany().HasForeignKey(d => d.PersonId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(d => d.AblaufAm).HasFilter("ablauf_am IS NOT NULL AND NOT geloescht");
    }
}
