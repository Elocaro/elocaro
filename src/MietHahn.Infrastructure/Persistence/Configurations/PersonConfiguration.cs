using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MietHahn.Domain.People;
using MietHahn.Infrastructure.Identity;

namespace MietHahn.Infrastructure.Persistence.Configurations;

internal sealed class PersonConfiguration : IEntityTypeConfiguration<Person>
{
    public void Configure(EntityTypeBuilder<Person> builder)
    {
        builder.ToTable("people");
        builder.Property(p => p.Salutation).HasConversion<string>().HasMaxLength(10);
        builder.Property(p => p.Title).HasMaxLength(50);
        builder.Property(p => p.FirstName).HasMaxLength(100).UseCollation(MietHahnDbContext.GermanCollation);
        builder.Property(p => p.LastName).HasMaxLength(200).UseCollation(MietHahnDbContext.GermanCollation);
        builder.Property(p => p.Company).HasMaxLength(200).UseCollation(MietHahnDbContext.GermanCollation);
        builder.Property(p => p.ContactType).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.Occupation).HasMaxLength(100);
        builder.Property(p => p.Email).HasMaxLength(254);
        builder.Property(p => p.Phone).HasMaxLength(50);
        builder.Property(p => p.Mobile).HasMaxLength(50);
        builder.Property(p => p.Notes).HasMaxLength(4000);
        builder.Ignore(p => p.DisplayName);
        builder.Ignore(p => p.SortName);
        builder.Ignore(p => p.IsAnonymized);
        builder.HasIndex(p => p.LastName);

        // An account belongs to at most one person.
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(p => p.UserId).IsUnique().HasFilter("user_id IS NOT NULL AND NOT deleted");

        builder.HasMany(p => p.Addresses).WithOne().HasForeignKey(a => a.PersonId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(p => p.BankDetails).WithOne().HasForeignKey(b => b.PersonId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PersonAddressConfiguration : IEntityTypeConfiguration<PersonAddress>
{
    public void Configure(EntityTypeBuilder<PersonAddress> builder)
    {
        builder.ToTable("person_addresses");
        builder.Property(a => a.AddressLine2).HasMaxLength(200);
        builder.Property(a => a.Street).HasMaxLength(200);
        builder.Property(a => a.PostalCode).HasMaxLength(10);
        builder.Property(a => a.City).HasMaxLength(100);
        builder.Property(a => a.Country).HasMaxLength(100);
        builder.Ignore(a => a.SingleLine);
    }
}

internal sealed class BankDetailsConfiguration : IEntityTypeConfiguration<BankDetails>
{
    public void Configure(EntityTypeBuilder<BankDetails> builder)
    {
        builder.ToTable("bank_details");
        builder.Property(b => b.Iban).HasMaxLength(42);
        builder.Property(b => b.AccountHolder).HasMaxLength(200);
        builder.Property(b => b.Bic).HasMaxLength(11);

        // Payment matching searches by IBAN.
        builder.HasIndex(b => b.Iban);
    }
}
