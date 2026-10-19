using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MietHahn.Domain.Leases;
using MietHahn.Domain.People;
using MietHahn.Domain.Properties;

namespace MietHahn.Infrastructure.Persistence.Configurations;

internal sealed class LeaseConfiguration : IEntityTypeConfiguration<Lease>
{
    public void Configure(EntityTypeBuilder<Lease> builder)
    {
        builder.ToTable("leases");
        builder.Property(v => v.TerminatedBy).HasConversion<string>().HasMaxLength(20);
        builder.Property(v => v.BillingType).HasConversion<string>().HasMaxLength(20);
        builder.Property(v => v.PaymentMethod).HasConversion<string>().HasMaxLength(20);
        builder.Property(v => v.DepositNote).HasMaxLength(1000);
        builder.Property(v => v.Notes).HasMaxLength(4000);
        builder.Property(v => v.RentModel).HasConversion<string>().HasMaxLength(20);
        builder.Property(v => v.IndexBaseValue).HasPrecision(6, 1);
        builder.Property(v => v.ExcludedCostTypes).HasColumnType("text[]").HasDefaultValueSql("'{}'");
        builder.Ignore(v => v.Start);
        builder.Ignore(v => v.End);
        builder.Ignore(v => v.Tenant);
        builder.Ignore(v => v.ReceivesOperatingCostStatement);
        builder.Ignore(v => v.ReceivesHeatingCostStatement);
        builder.HasIndex(v => v.RentalUnitId);

        builder.HasOne<Unit>().WithMany().HasForeignKey(v => v.UnitId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Room>().WithMany().HasForeignKey(v => v.RoomId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(v => v.Parties).WithOne().HasForeignKey(p => p.LeaseId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(v => v.Components).WithOne().HasForeignKey(b => b.LeaseId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(v => v.People).WithOne().HasForeignKey(p => p.LeaseId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(v => v.RentSteps).WithOne().HasForeignKey(s => s.LeaseId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class RentStepConfiguration : IEntityTypeConfiguration<RentStep>
{
    public void Configure(EntityTypeBuilder<RentStep> builder)
    {
        builder.ToTable("rent_steps");
        builder.HasIndex(s => new { s.LeaseId, s.From }).IsUnique().HasFilter("NOT deleted");
    }
}

internal sealed class CpiValueConfiguration : IEntityTypeConfiguration<CpiValue>
{
    public void Configure(EntityTypeBuilder<CpiValue> builder)
    {
        builder.ToTable("cpi_values");
        builder.Property(v => v.Value).HasPrecision(6, 1);
        builder.Property(v => v.Source).HasMaxLength(50);
        builder.HasIndex(v => v.Month).IsUnique().HasFilter("NOT deleted");
    }
}

internal sealed class LeasePartyConfiguration : IEntityTypeConfiguration<LeaseParty>
{
    public void Configure(EntityTypeBuilder<LeaseParty> builder)
    {
        builder.ToTable("lease_parties");
        builder.Property(p => p.Role).HasConversion<string>().HasMaxLength(20);
        builder.HasOne<Person>().WithMany().HasForeignKey(p => p.PersonId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(p => new { p.LeaseId, p.PersonId }).IsUnique().HasFilter("NOT deleted");
    }
}

internal sealed class RentComponentConfiguration : IEntityTypeConfiguration<RentComponent>
{
    public void Configure(EntityTypeBuilder<RentComponent> builder)
    {
        builder.ToTable("rent_components");
        builder.Property(b => b.Kind).HasConversion<string>().HasMaxLength(40);
        builder.Property(b => b.Name).HasMaxLength(100);
    }
}

internal sealed class OccupantCountConfiguration : IEntityTypeConfiguration<OccupantCount>
{
    public void Configure(EntityTypeBuilder<OccupantCount> builder) => builder.ToTable("occupant_counts");
}
