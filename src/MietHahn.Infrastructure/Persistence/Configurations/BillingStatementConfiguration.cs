using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MietHahn.Domain.Properties;
using MietHahn.Domain.Statements;

namespace MietHahn.Infrastructure.Persistence.Configurations;

internal sealed class BillingStatementConfiguration : IEntityTypeConfiguration<BillingStatement>
{
    public void Configure(EntityTypeBuilder<BillingStatement> builder)
    {
        builder.ToTable("billing_statements");
        builder.Property(s => s.Snapshot).HasColumnType("json");
        builder.Property(s => s.TotalCosts).HasPrecision(18, 2);
        builder.Property(s => s.LandlordTotal).HasPrecision(18, 2);
        builder.Property(s => s.Note).HasMaxLength(2000);
        builder.HasOne<Property>().WithMany().HasForeignKey(s => s.PropertyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BillingStatement>().WithMany().HasForeignKey(s => s.SupersedesId).OnDelete(DeleteBehavior.Restrict);
        // The unique index over the period and revision (complex type columns) is created as SQL in the migration.
        builder.HasIndex(s => s.PropertyId);
    }
}
