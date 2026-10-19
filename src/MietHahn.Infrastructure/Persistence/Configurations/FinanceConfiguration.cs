using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MietHahn.Domain.Finance;
using MietHahn.Domain.Leases;

namespace MietHahn.Infrastructure.Persistence.Configurations;

internal sealed class ChargeConfiguration : IEntityTypeConfiguration<Charge>
{
    public void Configure(EntityTypeBuilder<Charge> builder)
    {
        builder.ToTable("charges");
        builder.Property(s => s.Kind).HasConversion<string>().HasMaxLength(30);
        builder.Property(s => s.Component).HasConversion<string>().HasMaxLength(40);
        builder.Property(s => s.Name).HasMaxLength(200);
        builder.Property(s => s.Note).HasMaxLength(1000);
        builder.HasOne<Lease>().WithMany().HasForeignKey(s => s.LeaseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(s => new { s.LeaseId, s.Due });

        // The job may create only one monthly rent per lease, month and component (idempotent even with parallel runs).
        builder.HasIndex(s => new { s.LeaseId, s.ServiceMonth, s.Component }).IsUnique()
            .HasFilter("kind = 'Rent' AND NOT deleted")
            .HasDatabaseName("ix_charges_monthly_rent_unique");
    }
}

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments");
        builder.Property(z => z.Origin).HasConversion<string>().HasMaxLength(20);
        builder.Property(z => z.Purpose).HasMaxLength(500);
        builder.HasOne<Lease>().WithMany().HasForeignKey(z => z.LeaseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Charge>().WithMany().HasForeignKey(z => z.ChargeId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(z => new { z.LeaseId, z.Date });
    }
}

internal sealed class FinanceSettingsConfiguration : IEntityTypeConfiguration<FinanceSettings>
{
    public void Configure(EntityTypeBuilder<FinanceSettings> builder)
    {
        builder.ToTable("finance_settings");
        builder.Property(f => f.Allocation).HasConversion<string>().HasMaxLength(30);
    }
}
