using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MietHahn.Domain.Verwaltung;

namespace MietHahn.Infrastructure.Persistenz.Konfigurationen;

internal sealed class VermieterprofilKonfiguration : IEntityTypeConfiguration<Vermieterprofil>
{
    public void Configure(EntityTypeBuilder<Vermieterprofil> builder)
    {
        builder.ToTable("vermieterprofile");
        builder.Property(v => v.Name).HasMaxLength(200);
        builder.Property(v => v.Zusatz).HasMaxLength(200);
        builder.Property(v => v.Strasse).HasMaxLength(200);
        builder.Property(v => v.Plz).HasMaxLength(10);
        builder.Property(v => v.Ort).HasMaxLength(100);
        builder.Property(v => v.Telefon).HasMaxLength(50);
        builder.Property(v => v.Email).HasMaxLength(254);
        builder.Property(v => v.Iban).HasMaxLength(34);
        builder.Property(v => v.Bic).HasMaxLength(11);
        builder.Property(v => v.Bank).HasMaxLength(100);
        builder.Property(v => v.Steuernummer).HasMaxLength(30);
        builder.Ignore(v => v.Ruecksendezeile);
    }
}
