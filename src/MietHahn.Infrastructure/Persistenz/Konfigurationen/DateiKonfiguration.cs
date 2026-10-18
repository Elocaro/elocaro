using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MietHahn.Domain.Dateien;

namespace MietHahn.Infrastructure.Persistenz.Konfigurationen;

internal sealed class DateiKonfiguration : IEntityTypeConfiguration<Datei>
{
    public void Configure(EntityTypeBuilder<Datei> builder)
    {
        builder.ToTable("dateien");
        builder.Property(d => d.Name).HasMaxLength(Datei.MaxNameLaenge);
        builder.Property(d => d.MimeTyp).HasMaxLength(100);
        builder.Property(d => d.Sha256).HasMaxLength(64).IsFixedLength();
        builder.HasIndex(d => d.Sha256);
        builder.Ignore(d => d.IstBild);
    }
}
