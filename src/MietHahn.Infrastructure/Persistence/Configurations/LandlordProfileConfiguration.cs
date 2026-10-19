using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MietHahn.Domain.Admin;

namespace MietHahn.Infrastructure.Persistence.Configurations;

internal sealed class LandlordProfileConfiguration : IEntityTypeConfiguration<LandlordProfile>
{
    public void Configure(EntityTypeBuilder<LandlordProfile> builder)
    {
        builder.ToTable("landlord_profiles");
        builder.Property(v => v.Name).HasMaxLength(200);
        builder.Property(v => v.AddressLine2).HasMaxLength(200);
        builder.Property(v => v.Street).HasMaxLength(200);
        builder.Property(v => v.PostalCode).HasMaxLength(10);
        builder.Property(v => v.City).HasMaxLength(100);
        builder.Property(v => v.Phone).HasMaxLength(50);
        builder.Property(v => v.Email).HasMaxLength(254);
        builder.Property(v => v.Iban).HasMaxLength(34);
        builder.Property(v => v.Bic).HasMaxLength(11);
        builder.Property(v => v.Bank).HasMaxLength(100);
        builder.Property(v => v.TaxNumber).HasMaxLength(30);
        builder.Ignore(v => v.ReturnAddressLine);
    }
}
