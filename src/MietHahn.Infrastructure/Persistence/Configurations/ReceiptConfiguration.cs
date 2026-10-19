using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MietHahn.Domain.Files;
using MietHahn.Domain.People;
using MietHahn.Domain.Properties;
using MietHahn.Domain.Receipts;

namespace MietHahn.Infrastructure.Persistence.Configurations;

internal sealed class ReceiptConfiguration : IEntityTypeConfiguration<Receipt>
{
    public void Configure(EntityTypeBuilder<Receipt> builder)
    {
        builder.ToTable("receipts");
        builder.Property(b => b.SupplierName).HasMaxLength(200).UseCollation(MietHahnDbContext.GermanCollation);
        builder.Property(b => b.InvoiceNumber).HasMaxLength(100);
        builder.Property(b => b.FuelUnit).HasMaxLength(10);
        builder.Property(b => b.FuelQuantity).HasPrecision(14, 3);
        builder.Property(b => b.Co2Kg).HasPrecision(14, 3);
        builder.Property(b => b.Note).HasMaxLength(2000);
        builder.Ignore(b => b.ServicePeriod);
        builder.Ignore(b => b.Allocated);
        builder.Ignore(b => b.IsFullyAllocated);
        builder.HasOne<Person>().WithMany().HasForeignKey(b => b.SupplierId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(b => b.Files).WithOne().HasForeignKey(d => d.ReceiptId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(b => b.Items).WithOne().HasForeignKey(p => p.ReceiptId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(b => b.InvoiceDate);
    }
}

internal sealed class ReceiptFileConfiguration : IEntityTypeConfiguration<ReceiptFile>
{
    public void Configure(EntityTypeBuilder<ReceiptFile> builder)
    {
        builder.ToTable("receipt_files");
        builder.HasOne<StoredFile>().WithMany().HasForeignKey(d => d.FileId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CostItemConfiguration : IEntityTypeConfiguration<CostItem>
{
    public void Configure(EntityTypeBuilder<CostItem> builder)
    {
        builder.ToTable("cost_items");
        builder.Property(p => p.CostType).HasMaxLength(40);
        builder.Property(p => p.Section35a).HasConversion<string>().HasMaxLength(40);
        builder.Property(p => p.Note).HasMaxLength(500);
        builder.HasOne<Property>().WithMany().HasForeignKey(p => p.PropertyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Unit>().WithMany().HasForeignKey(p => p.UnitId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CostSubtype>().WithMany().HasForeignKey(p => p.SubtypeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(p => new { p.PropertyId, p.CostType });
    }
}

internal sealed class SupplierCostTypeRuleConfiguration : IEntityTypeConfiguration<SupplierCostTypeRule>
{
    public void Configure(EntityTypeBuilder<SupplierCostTypeRule> builder)
    {
        builder.ToTable("supplier_cost_type_rules");
        builder.Property(r => r.SupplierKey).HasMaxLength(250);
        builder.Property(r => r.CostType).HasMaxLength(40);
        builder.HasIndex(r => r.SupplierKey).IsUnique().HasFilter("NOT deleted");
    }
}
