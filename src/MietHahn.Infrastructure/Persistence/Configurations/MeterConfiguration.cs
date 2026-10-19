using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MietHahn.Domain.Files;
using MietHahn.Domain.Metering;
using MietHahn.Domain.Properties;

namespace MietHahn.Infrastructure.Persistence.Configurations;

internal sealed class MeterConfiguration : IEntityTypeConfiguration<Meter>
{
    public void Configure(EntityTypeBuilder<Meter> builder)
    {
        builder.ToTable("meters");
        builder.Property(z => z.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(z => z.MeasurementUnit).HasConversion<string>().HasMaxLength(20);
        builder.Property(z => z.Name).HasMaxLength(200).UseCollation(MietHahnDbContext.GermanCollation);
        builder.Property(z => z.Number).HasMaxLength(50);
        builder.Property(z => z.Location).HasMaxLength(200);
        builder.Property(z => z.Notes).HasMaxLength(2000);
        builder.HasIndex(z => z.Number);

        builder.HasOne<Property>().WithMany().HasForeignKey(z => z.PropertyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Unit>().WithMany().HasForeignKey(z => z.UnitId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Room>().WithMany().HasForeignKey(z => z.RoomId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Meter>().WithMany().HasForeignKey(z => z.MainMeterId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Meter>().WithMany().HasForeignKey(z => z.PredecessorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(z => z.Readings).WithOne().HasForeignKey(s => s.MeterId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class MeterReadingConfiguration : IEntityTypeConfiguration<MeterReading>
{
    public void Configure(EntityTypeBuilder<MeterReading> builder)
    {
        builder.ToTable("meter_readings");
        builder.Property(s => s.Value).HasPrecision(14, 3);
        builder.Property(s => s.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.ReadBy).HasMaxLength(256);
        builder.Property(s => s.Note).HasMaxLength(1000);
        builder.HasOne<StoredFile>().WithMany().HasForeignKey(s => s.PhotoFileId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(s => new { s.MeterId, s.Date }).IsUnique().HasFilter("NOT deleted");
    }
}
