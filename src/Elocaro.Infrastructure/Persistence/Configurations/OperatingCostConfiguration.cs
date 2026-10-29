using Elocaro.Domain.OperatingCosts;
using Elocaro.Domain.Properties;
using Elocaro.Domain.Receipts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elocaro.Infrastructure.Persistence.Configurations;

internal sealed class CostAllocationRuleConfiguration : IEntityTypeConfiguration<CostAllocationRule>
{
    public void Configure(EntityTypeBuilder<CostAllocationRule> builder)
    {
        builder.ToTable("cost_allocation_rules");
        builder.Property(r => r.CostType).HasMaxLength(40);
        builder.Property(r => r.Key).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.MeterType).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.PreDeductionPercent).HasPrecision(5, 2);
        builder.Property(r => r.PreDeductionNote).HasMaxLength(200);
        builder.HasOne<Property>().WithMany().HasForeignKey(r => r.PropertyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(r => r.Shares).WithOne().HasForeignKey(a => a.RuleId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(r => new { r.PropertyId, r.CostType });
    }
}

internal sealed class CostAllocationShareConfiguration : IEntityTypeConfiguration<CostAllocationShare>
{
    public void Configure(EntityTypeBuilder<CostAllocationShare> builder)
    {
        builder.ToTable("cost_allocation_shares");
        builder.Property(a => a.Percent).HasPrecision(5, 2);
        builder.HasOne<Unit>().WithMany().HasForeignKey(a => a.UnitId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CostSubtypeConfiguration : IEntityTypeConfiguration<CostSubtype>
{
    public void Configure(EntityTypeBuilder<CostSubtype> builder)
    {
        builder.ToTable("cost_subtypes");
        builder.Property(a => a.CostType).HasMaxLength(40);
        builder.Property(a => a.Name).HasMaxLength(100).UseCollation(ElocaroDbContext.GermanCollation);
        builder.HasIndex(a => new { a.CostType, a.Name }).IsUnique().HasFilter("NOT deleted");
    }
}
