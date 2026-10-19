using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MietHahn.Domain.Bank;

namespace MietHahn.Infrastructure.Persistence.Configurations;

internal sealed class BankAccountConfiguration : IEntityTypeConfiguration<BankAccount>
{
    public void Configure(EntityTypeBuilder<BankAccount> builder)
    {
        builder.ToTable("bank_accounts");
        builder.Property(k => k.Name).HasMaxLength(100).UseCollation(MietHahnDbContext.GermanCollation);
        builder.Property(k => k.Iban).HasMaxLength(34);
        builder.Property(k => k.Bic).HasMaxLength(11);
        builder.Property(k => k.Bank).HasMaxLength(100);
        builder.Property(k => k.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(k => k.PropertyIds).HasColumnType("uuid[]");
        builder.HasIndex(k => k.Iban).IsUnique().HasFilter("NOT deleted");
    }
}

internal sealed class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.ToTable("bank_transactions");
        builder.Property(u => u.Currency).HasMaxLength(3);
        builder.Property(u => u.CounterpartyName).HasMaxLength(200);
        builder.Property(u => u.CounterpartyIban).HasMaxLength(34);
        builder.Property(u => u.CounterpartyBic).HasMaxLength(11);
        builder.Property(u => u.Purpose).HasMaxLength(1000);
        builder.Property(u => u.BookingText).HasMaxLength(200);
        builder.Property(u => u.EndToEndId).HasMaxLength(100);
        builder.Property(u => u.MandateReference).HasMaxLength(100);
        builder.Property(u => u.CreditorId).HasMaxLength(50);
        builder.Property(u => u.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(u => u.Note).HasMaxLength(1000);
        builder.Property(u => u.Hash).HasMaxLength(64);
        builder.HasOne<BankAccount>().WithMany().HasForeignKey(u => u.BankAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BankImport>().WithMany().HasForeignKey(u => u.BankImportId).OnDelete(DeleteBehavior.SetNull);

        // Duplicate detection also for concurrent imports; also applies to deleted transactions (otherwise they would reappear on the next import).
        builder.HasIndex(u => new { u.BankAccountId, u.Hash }).IsUnique();
        builder.HasIndex(u => new { u.BankAccountId, u.Status, u.BookingDate });
    }
}

internal sealed class BankImportConfiguration : IEntityTypeConfiguration<BankImport>
{
    public void Configure(EntityTypeBuilder<BankImport> builder)
    {
        builder.ToTable("bank_imports");
        builder.Property(i => i.FileName).HasMaxLength(255);
        builder.Property(i => i.Format).HasConversion<string>().HasMaxLength(20);
        builder.Property(i => i.Profile).HasMaxLength(100);
        builder.Property(i => i.Status).HasConversion<string>().HasMaxLength(20);
        builder.HasOne<BankAccount>().WithMany().HasForeignKey(i => i.BankAccountId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CsvProfileConfiguration : IEntityTypeConfiguration<CsvProfile>
{
    public void Configure(EntityTypeBuilder<CsvProfile> builder)
    {
        builder.ToTable("csv_profiles");
        builder.Property(p => p.Name).HasMaxLength(100);
        builder.Property(p => p.Charset).HasMaxLength(20);
        builder.Property(p => p.DateFormat).HasMaxLength(100);
        builder.Property(p => p.ParsePattern).HasMaxLength(500);
        builder.Ignore(p => p.IsDefault);
        foreach (var column in typeof(CsvProfile).GetProperties().Where(p => p.Name.StartsWith("Spalte", StringComparison.Ordinal)))
        {
            builder.Property(column.Name).HasMaxLength(200);
        }

        builder.HasIndex(p => p.Name).IsUnique().HasFilter("NOT deleted");
    }
}
