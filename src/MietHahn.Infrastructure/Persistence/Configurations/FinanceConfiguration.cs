using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MietHahn.Domain.Files;
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

        // Defaults also apply to the existing record when the columns are added.
        builder.Property(f => f.DunningGraceDays).HasDefaultValue(DunningRules.Default.GraceDays);
        builder.Property(f => f.DaysUntilFirstNotice).HasDefaultValue(DunningRules.Default.DaysUntilFirstNotice);
        builder.Property(f => f.DaysUntilSecondNotice).HasDefaultValue(DunningRules.Default.DaysUntilSecondNotice);
        builder.Property(f => f.DunningDeadlineDays).HasDefaultValue(DunningRules.Default.DeadlineDays);
        builder.Property(f => f.DunningMinimumAmount).HasDefaultValue(DunningRules.Default.MinimumAmount);
        builder.Property(f => f.InterestMarginPoints).HasPrecision(5, 2).HasDefaultValue(DunningRules.Default.InterestMarginPoints);
        builder.Property(f => f.BaseRate).HasPrecision(5, 2);
        builder.Ignore(f => f.DunningRules);
    }
}

internal sealed class DunningConfiguration : IEntityTypeConfiguration<Dunning>
{
    public void Configure(EntityTypeBuilder<Dunning> builder)
    {
        builder.ToTable("dunnings");
        builder.Property(m => m.Level).HasConversion<string>().HasMaxLength(20);
        builder.Property(m => m.Status).HasConversion<string>().HasMaxLength(20);
        builder.HasOne<Lease>().WithMany().HasForeignKey(m => m.LeaseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StoredFile>().WithMany().HasForeignKey(m => m.FileId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(m => new { m.LeaseId, m.Status });

        // At most one open proposal per tenancy (idempotent even with parallel job runs).
        builder.HasIndex(m => m.LeaseId).IsUnique()
            .HasFilter("status = 'Proposed' AND NOT deleted")
            .HasDatabaseName("ix_dunnings_proposal_unique");
    }
}

internal sealed class DunningHoldConfiguration : IEntityTypeConfiguration<DunningHold>
{
    public void Configure(EntityTypeBuilder<DunningHold> builder)
    {
        builder.ToTable("dunning_holds");
        builder.Property(s => s.Reason).HasMaxLength(500);
        builder.HasOne<Lease>().WithMany().HasForeignKey(s => s.LeaseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(s => s.LeaseId);
    }
}

internal sealed class DepositEntryConfiguration : IEntityTypeConfiguration<MietHahn.Domain.Deposits.DepositEntry>
{
    public void Configure(EntityTypeBuilder<MietHahn.Domain.Deposits.DepositEntry> builder)
    {
        builder.ToTable("deposit_entries");
        builder.Property(e => e.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Amount).HasPrecision(12, 2);
        builder.Property(e => e.Note).HasMaxLength(500);
        builder.Ignore(e => e.Effect);
        builder.HasOne<Lease>().WithMany().HasForeignKey(e => e.LeaseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.LeaseId, e.Date });
    }
}
