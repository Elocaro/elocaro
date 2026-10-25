using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MietHahn.Domain.OperatingCosts;
using MietHahn.Domain.Properties;

namespace MietHahn.Infrastructure.Persistence.Configurations;

internal sealed class SupplyContractConfiguration : IEntityTypeConfiguration<SupplyContract>
{
    public void Configure(EntityTypeBuilder<SupplyContract> builder)
    {
        builder.ToTable("supply_contracts");
        builder.Property(c => c.Name).HasMaxLength(100);
        builder.Property(c => c.Supplier).HasMaxLength(100);
        builder.Property(c => c.CostType).HasMaxLength(40);
        builder.Property(c => c.MeterType).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Basis).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.PriceUnit).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.HeatingKind).HasConversion<string>().HasMaxLength(30);
        builder.HasOne<Property>().WithMany().HasForeignKey(c => c.PropertyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(c => c.Tariffs).WithOne().HasForeignKey(t => t.ContractId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(c => c.PropertyId);
    }
}

internal sealed class SupplyTariffConfiguration : IEntityTypeConfiguration<SupplyTariff>
{
    public void Configure(EntityTypeBuilder<SupplyTariff> builder)
    {
        builder.ToTable("supply_tariffs");
        builder.Property(t => t.UnitPrice).HasPrecision(12, 6);
        builder.Property(t => t.BaseFeePerMonth).HasPrecision(12, 4);
        builder.Property(t => t.Note).HasMaxLength(200);
    }
}
