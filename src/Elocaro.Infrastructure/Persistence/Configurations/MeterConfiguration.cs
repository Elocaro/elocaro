using Elocaro.Domain.Files;
using Elocaro.Domain.Metering;
using Elocaro.Domain.Properties;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elocaro.Infrastructure.Persistence.Configurations;

internal sealed class MeterConfiguration : IEntityTypeConfiguration<Meter>
{
    public void Configure(EntityTypeBuilder<Meter> builder)
    {
        builder.ToTable("meters");
        builder.Property(z => z.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(z => z.MeasurementUnit).HasConversion<string>().HasMaxLength(20);
        builder.Property(z => z.Name).HasMaxLength(200).UseCollation(ElocaroDbContext.GermanCollation);
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
        builder.HasMany(z => z.Terms).WithOne().HasForeignKey(t => t.MeterId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class MeterTermConfiguration : IEntityTypeConfiguration<MeterTerm>
{
    public void Configure(EntityTypeBuilder<MeterTerm> builder)
    {
        builder.ToTable("meter_terms");
        builder.HasOne<Meter>().WithMany().HasForeignKey(t => t.SourceMeterId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(t => new { t.MeterId, t.SourceMeterId }).IsUnique().HasFilter("NOT deleted");
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

internal sealed class ConsumptionInformationDispatchConfiguration : IEntityTypeConfiguration<ConsumptionInformationDispatch>
{
    public void Configure(EntityTypeBuilder<ConsumptionInformationDispatch> builder)
    {
        builder.ToTable("consumption_information_dispatches");
        builder.HasOne<Elocaro.Domain.Leases.Lease>().WithMany().HasForeignKey(d => d.LeaseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(d => new { d.LeaseId, d.Year, d.Month }).IsUnique().HasFilter("NOT deleted");
    }
}

internal sealed class ReadingReportConfiguration : IEntityTypeConfiguration<ReadingReport>
{
    public void Configure(EntityTypeBuilder<ReadingReport> builder)
    {
        builder.ToTable("reading_reports");
        builder.Property(r => r.Value).HasPrecision(14, 3);
        builder.Property(r => r.ReportedBy).HasMaxLength(200);
        builder.Property(r => r.Note).HasMaxLength(1000);
        builder.Property(r => r.DecisionNote).HasMaxLength(500);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        builder.HasOne<Elocaro.Domain.Metering.Meter>().WithMany().HasForeignKey(r => r.MeterId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(r => new { r.MeterId, r.Status });
    }
}

internal sealed class ReadingReminderConfiguration : IEntityTypeConfiguration<ReadingReminder>
{
    public void Configure(EntityTypeBuilder<ReadingReminder> builder)
    {
        builder.ToTable("reading_reminders");
        builder.HasOne<Elocaro.Domain.Leases.Lease>().WithMany().HasForeignKey(d => d.LeaseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(d => new { d.LeaseId, d.Year }).IsUnique().HasFilter("NOT deleted");
    }
}
