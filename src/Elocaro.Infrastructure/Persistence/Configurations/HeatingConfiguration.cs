using Elocaro.Domain.Heating;
using Elocaro.Domain.Metering;
using Elocaro.Domain.Properties;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elocaro.Infrastructure.Persistence.Configurations;

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
        // Brennwert/Zustandszahl per year as one JSON document; the comparer looks at the serialized text so that edits are noticed.
        var comparer = new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<List<FuelEnergyYear>>(
            (a, b) => System.Text.Json.JsonSerializer.Serialize(a, (System.Text.Json.JsonSerializerOptions?)null) == System.Text.Json.JsonSerializer.Serialize(b, (System.Text.Json.JsonSerializerOptions?)null),
            c => System.Text.Json.JsonSerializer.Serialize(c, (System.Text.Json.JsonSerializerOptions?)null).GetHashCode(StringComparison.Ordinal),
            c => System.Text.Json.JsonSerializer.Deserialize<List<FuelEnergyYear>>(System.Text.Json.JsonSerializer.Serialize(c, (System.Text.Json.JsonSerializerOptions?)null), (System.Text.Json.JsonSerializerOptions?)null)!);
        builder.Property(h => h.EnergyYears)
            .HasConversion(c => System.Text.Json.JsonSerializer.Serialize(c, (System.Text.Json.JsonSerializerOptions?)null), s => System.Text.Json.JsonSerializer.Deserialize<List<FuelEnergyYear>>(s, (System.Text.Json.JsonSerializerOptions?)null)!, comparer)
            .HasColumnType("jsonb").HasDefaultValueSql("'[]'::jsonb");
        builder.Property(h => h.CarbonKgPerKwh).HasPrecision(8, 5);
        builder.Property(h => h.SplitBasis).HasConversion<string>().HasMaxLength(20);
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
