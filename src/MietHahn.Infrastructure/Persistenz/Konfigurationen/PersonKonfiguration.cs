using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MietHahn.Domain.Personen;
using MietHahn.Infrastructure.Identitaet;

namespace MietHahn.Infrastructure.Persistenz.Konfigurationen;

internal sealed class PersonKonfiguration : IEntityTypeConfiguration<Person>
{
    public void Configure(EntityTypeBuilder<Person> builder)
    {
        builder.ToTable("personen");
        builder.Property(p => p.Anrede).HasConversion<string>().HasMaxLength(10);
        builder.Property(p => p.Titel).HasMaxLength(50);
        builder.Property(p => p.Vorname).HasMaxLength(100).UseCollation(MietHahnDbContext.DeutscheKollation);
        builder.Property(p => p.Nachname).HasMaxLength(200).UseCollation(MietHahnDbContext.DeutscheKollation);
        builder.Property(p => p.Firma).HasMaxLength(200).UseCollation(MietHahnDbContext.DeutscheKollation);
        builder.Property(p => p.Kontaktart).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.Taetigkeit).HasMaxLength(100);
        builder.Property(p => p.Email).HasMaxLength(254);
        builder.Property(p => p.Telefon).HasMaxLength(50);
        builder.Property(p => p.Mobil).HasMaxLength(50);
        builder.Property(p => p.Notizen).HasMaxLength(4000);
        builder.Ignore(p => p.Anzeigename);
        builder.Ignore(p => p.Sortiername);
        builder.Ignore(p => p.IstAnonymisiert);
        builder.HasIndex(p => p.Nachname);

        // Ein Konto gehört zu höchstens einer Person.
        builder.HasOne<Benutzer>().WithMany().HasForeignKey(p => p.BenutzerId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(p => p.BenutzerId).IsUnique().HasFilter("benutzer_id IS NOT NULL AND NOT geloescht");

        builder.HasMany(p => p.Anschriften).WithOne().HasForeignKey(a => a.PersonId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(p => p.Bankverbindungen).WithOne().HasForeignKey(b => b.PersonId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PersonAnschriftKonfiguration : IEntityTypeConfiguration<PersonAnschrift>
{
    public void Configure(EntityTypeBuilder<PersonAnschrift> builder)
    {
        builder.ToTable("person_anschriften");
        builder.Property(a => a.Zusatz).HasMaxLength(200);
        builder.Property(a => a.Strasse).HasMaxLength(200);
        builder.Property(a => a.Plz).HasMaxLength(10);
        builder.Property(a => a.Ort).HasMaxLength(100);
        builder.Property(a => a.Land).HasMaxLength(100);
        builder.Ignore(a => a.Einzeilig);
    }
}

internal sealed class BankverbindungKonfiguration : IEntityTypeConfiguration<Bankverbindung>
{
    public void Configure(EntityTypeBuilder<Bankverbindung> builder)
    {
        builder.ToTable("bankverbindungen");
        builder.Property(b => b.Iban).HasMaxLength(42);
        builder.Property(b => b.Kontoinhaber).HasMaxLength(200);
        builder.Property(b => b.Bic).HasMaxLength(11);

        // Zahlungszuordnung sucht nach IBAN.
        builder.HasIndex(b => b.Iban);
    }
}
