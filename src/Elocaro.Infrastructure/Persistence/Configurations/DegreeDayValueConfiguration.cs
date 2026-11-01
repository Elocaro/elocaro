using Elocaro.Domain.Heating;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elocaro.Infrastructure.Persistence.Configurations;

internal sealed class DegreeDayValueConfiguration : IEntityTypeConfiguration<DegreeDayValue>
{
    public void Configure(EntityTypeBuilder<DegreeDayValue> builder)
    {
        builder.ToTable("degree_day_values");
        builder.Property(v => v.StationId).HasMaxLength(10);
        builder.Property(v => v.StationName).HasMaxLength(100);
        builder.Property(v => v.DegreeDays).HasPrecision(8, 1);
        builder.Property(v => v.LongTermMean).HasPrecision(8, 1);
        builder.HasIndex(v => new { v.StationId, v.Month }).IsUnique();
    }
}
