using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MietHahn.Domain.Properties;
using MietHahn.Domain.Tax;

namespace MietHahn.Infrastructure.Persistence.Configurations;

internal sealed class PropertyAcquisitionConfiguration : IEntityTypeConfiguration<PropertyAcquisition>
{
    public void Configure(EntityTypeBuilder<PropertyAcquisition> builder)
    {
        builder.ToTable("property_acquisitions");
        builder.Property(a => a.PurchasePrice).HasPrecision(18, 2);
        builder.Property(a => a.Notary).HasPrecision(18, 2);
        builder.Property(a => a.LandRegistry).HasPrecision(18, 2);
        builder.Property(a => a.TransferTax).HasPrecision(18, 2);
        builder.Property(a => a.Broker).HasPrecision(18, 2);
        builder.Property(a => a.LandSharePercent).HasPrecision(5, 2);
        builder.Property(a => a.RateOverridePercent).HasPrecision(5, 2);
        builder.Property(a => a.SpecialRatePercent).HasPrecision(5, 2);
        builder.Property(a => a.Notes).HasMaxLength(2000);
        builder.Ignore(a => a.AncillaryCosts);
        builder.HasOne<Property>().WithMany().HasForeignKey(a => a.PropertyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(a => a.PropertyId).IsUnique().HasFilter("NOT deleted");
    }
}

internal sealed class PropertyCostConfiguration : IEntityTypeConfiguration<PropertyCost>
{
    public void Configure(EntityTypeBuilder<PropertyCost> builder)
    {
        builder.ToTable("property_costs");
        builder.Property(k => k.Amount).HasPrecision(18, 2);
        builder.Property(k => k.Description).HasMaxLength(200);
        builder.HasOne<Property>().WithMany().HasForeignKey(k => k.PropertyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(k => new { k.PropertyId, k.Date });
    }
}

internal sealed class PropertyAssetConfiguration : IEntityTypeConfiguration<PropertyAsset>
{
    public void Configure(EntityTypeBuilder<PropertyAsset> builder)
    {
        builder.ToTable("property_assets");
        builder.Property(w => w.Name).HasMaxLength(200).UseCollation(MietHahnDbContext.GermanCollation);
        builder.Property(w => w.Cost).HasPrecision(18, 2);
        builder.HasOne<Property>().WithMany().HasForeignKey(w => w.PropertyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(w => new { w.PropertyId, w.AcquisitionDate });
    }
}
