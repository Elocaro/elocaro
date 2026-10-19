using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MietHahn.Domain.Heating;
using MietHahn.Domain.Metering;
using MietHahn.Domain.Properties;

namespace MietHahn.Infrastructure.Persistence.Configurations;

internal sealed class HeatingSettingsConfiguration : IEntityTypeConfiguration<HeatingSettings>
{
    public void Configure(EntityTypeBuilder<HeatingSettings> builder)
    {
        builder.ToTable("heating_settings");
        builder.Property(h => h.Fuel).HasConversion<string>().HasMaxLength(20);
        builder.Property(h => h.ConsumptionPercent).HasPrecision(5, 2);
        builder.Property(h => h.HotWaterFormulaFactor).HasPrecision(6, 3);
        builder.Property(h => h.HotWaterTemperature).HasPrecision(5, 1);
        builder.Property(h => h.FuelKwhPerUnit).HasPrecision(10, 4);
        builder.Property(h => h.OilValuation).HasConversion<string>().HasMaxLength(20);
        builder.Property(h => h.OilOpeningPricePerLiter).HasPrecision(10, 4);
        builder.Property(h => h.CarbonSpecialCase).HasConversion<string>().HasMaxLength(30);
        builder.Property(h => h.CarbonNote).HasMaxLength(500);
        builder.HasOne<Property>().WithMany().HasForeignKey(h => h.PropertyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Meter>().WithMany().HasForeignKey(h => h.HotWaterHeatMeterId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Meter>().WithMany().HasForeignKey(h => h.OilTankMeterId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(h => h.PropertyId).IsUnique().HasFilter("NOT deleted");
    }
}
