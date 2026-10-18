using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MietHahn.Infrastructure.Identitaet;

namespace MietHahn.Infrastructure.Persistenz.Konfigurationen;

internal sealed class AnmeldelinkKonfiguration : IEntityTypeConfiguration<Anmeldelink>
{
    public void Configure(EntityTypeBuilder<Anmeldelink> builder)
    {
        builder.ToTable("anmeldelinks");
        builder.Property(l => l.TokenHash).HasMaxLength(64).IsFixedLength();
        builder.HasIndex(l => l.TokenHash).IsUnique();
        builder.HasOne<Benutzer>().WithMany().HasForeignKey(l => l.BenutzerId).OnDelete(DeleteBehavior.Cascade);
    }
}
