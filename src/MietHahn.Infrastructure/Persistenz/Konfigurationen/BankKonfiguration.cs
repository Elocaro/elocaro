using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MietHahn.Domain.Bank;

namespace MietHahn.Infrastructure.Persistenz.Konfigurationen;

internal sealed class BankkontoKonfiguration : IEntityTypeConfiguration<Bankkonto>
{
    public void Configure(EntityTypeBuilder<Bankkonto> builder)
    {
        builder.ToTable("bankkonten");
        builder.Property(k => k.Name).HasMaxLength(100).UseCollation(MietHahnDbContext.DeutscheKollation);
        builder.Property(k => k.Iban).HasMaxLength(34);
        builder.Property(k => k.Bic).HasMaxLength(11);
        builder.Property(k => k.Bank).HasMaxLength(100);
        builder.Property(k => k.Typ).HasConversion<string>().HasMaxLength(20);
        builder.Property(k => k.ObjektIds).HasColumnType("uuid[]");
        builder.HasIndex(k => k.Iban).IsUnique().HasFilter("NOT geloescht");
    }
}

internal sealed class UmsatzKonfiguration : IEntityTypeConfiguration<Umsatz>
{
    public void Configure(EntityTypeBuilder<Umsatz> builder)
    {
        builder.ToTable("umsaetze");
        builder.Property(u => u.Waehrung).HasMaxLength(3);
        builder.Property(u => u.GegenName).HasMaxLength(200);
        builder.Property(u => u.GegenIban).HasMaxLength(34);
        builder.Property(u => u.GegenBic).HasMaxLength(11);
        builder.Property(u => u.Verwendungszweck).HasMaxLength(1000);
        builder.Property(u => u.Buchungstext).HasMaxLength(200);
        builder.Property(u => u.EndToEndId).HasMaxLength(100);
        builder.Property(u => u.Mandatsreferenz).HasMaxLength(100);
        builder.Property(u => u.GlaeubigerId).HasMaxLength(50);
        builder.Property(u => u.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(u => u.Notiz).HasMaxLength(1000);
        builder.Property(u => u.Hash).HasMaxLength(64);
        builder.HasOne<Bankkonto>().WithMany().HasForeignKey(u => u.BankkontoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Bankimport>().WithMany().HasForeignKey(u => u.BankimportId).OnDelete(DeleteBehavior.SetNull);

        // Duplikaterkennung auch bei gleichzeitigen Importen; gilt auch für gelöschte Umsätze (sonst kämen sie beim nächsten Import wieder).
        builder.HasIndex(u => new { u.BankkontoId, u.Hash }).IsUnique();
        builder.HasIndex(u => new { u.BankkontoId, u.Status, u.Buchungsdatum });
    }
}

internal sealed class BankimportKonfiguration : IEntityTypeConfiguration<Bankimport>
{
    public void Configure(EntityTypeBuilder<Bankimport> builder)
    {
        builder.ToTable("bankimporte");
        builder.Property(i => i.Dateiname).HasMaxLength(255);
        builder.Property(i => i.Format).HasConversion<string>().HasMaxLength(20);
        builder.Property(i => i.Profil).HasMaxLength(100);
        builder.Property(i => i.Status).HasConversion<string>().HasMaxLength(20);
        builder.HasOne<Bankkonto>().WithMany().HasForeignKey(i => i.BankkontoId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CsvProfilKonfiguration : IEntityTypeConfiguration<CsvProfil>
{
    public void Configure(EntityTypeBuilder<CsvProfil> builder)
    {
        builder.ToTable("csv_profile");
        builder.Property(p => p.Name).HasMaxLength(100);
        builder.Property(p => p.Zeichensatz).HasMaxLength(20);
        builder.Property(p => p.Datumsformat).HasMaxLength(100);
        builder.Property(p => p.Zerlegung).HasMaxLength(500);
        builder.Ignore(p => p.Vorgabe);
        foreach (var spalte in typeof(CsvProfil).GetProperties().Where(p => p.Name.StartsWith("Spalte", StringComparison.Ordinal)))
        {
            builder.Property(spalte.Name).HasMaxLength(200);
        }

        builder.HasIndex(p => p.Name).IsUnique().HasFilter("NOT geloescht");
    }
}
