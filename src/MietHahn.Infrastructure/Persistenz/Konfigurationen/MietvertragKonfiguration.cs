using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MietHahn.Domain.Objekte;
using MietHahn.Domain.Personen;
using MietHahn.Domain.Vertraege;

namespace MietHahn.Infrastructure.Persistenz.Konfigurationen;

internal sealed class MietvertragKonfiguration : IEntityTypeConfiguration<Mietvertrag>
{
    public void Configure(EntityTypeBuilder<Mietvertrag> builder)
    {
        builder.ToTable("mietvertraege");
        builder.Property(v => v.KuendigungDurch).HasConversion<string>().HasMaxLength(20);
        builder.Property(v => v.Abrechnungsart).HasConversion<string>().HasMaxLength(20);
        builder.Property(v => v.Zahlungsart).HasConversion<string>().HasMaxLength(20);
        builder.Property(v => v.KautionNotiz).HasMaxLength(1000);
        builder.Property(v => v.Notizen).HasMaxLength(4000);
        builder.Ignore(v => v.Beginn);
        builder.Ignore(v => v.Ende);
        builder.Ignore(v => v.Mieter);
        builder.Ignore(v => v.ErhaeltNebenkostenabrechnung);
        builder.Ignore(v => v.ErhaeltHeizkostenabrechnung);
        builder.HasIndex(v => v.MietobjektId);

        builder.HasOne<Einheit>().WithMany().HasForeignKey(v => v.EinheitId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Zimmer>().WithMany().HasForeignKey(v => v.ZimmerId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(v => v.Parteien).WithOne().HasForeignKey(p => p.MietvertragId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(v => v.Bestandteile).WithOne().HasForeignKey(b => b.MietvertragId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(v => v.Personen).WithOne().HasForeignKey(p => p.MietvertragId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class VertragsparteiKonfiguration : IEntityTypeConfiguration<Vertragspartei>
{
    public void Configure(EntityTypeBuilder<Vertragspartei> builder)
    {
        builder.ToTable("vertragsparteien");
        builder.Property(p => p.Rolle).HasConversion<string>().HasMaxLength(20);
        builder.HasOne<Person>().WithMany().HasForeignKey(p => p.PersonId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(p => new { p.MietvertragId, p.PersonId }).IsUnique().HasFilter("NOT geloescht");
    }
}

internal sealed class MietbestandteilKonfiguration : IEntityTypeConfiguration<Mietbestandteil>
{
    public void Configure(EntityTypeBuilder<Mietbestandteil> builder)
    {
        builder.ToTable("mietbestandteile");
        builder.Property(b => b.Art).HasConversion<string>().HasMaxLength(40);
        builder.Property(b => b.Bezeichnung).HasMaxLength(100);
    }
}

internal sealed class PersonenstandKonfiguration : IEntityTypeConfiguration<Personenstand>
{
    public void Configure(EntityTypeBuilder<Personenstand> builder) => builder.ToTable("personenstaende");
}
