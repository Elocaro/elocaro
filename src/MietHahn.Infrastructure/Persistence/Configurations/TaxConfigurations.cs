using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MietHahn.Domain.Properties;
using MietHahn.Domain.Tax;

namespace MietHahn.Infrastructure.Persistence.Configurations;

internal sealed class PropertyAcquisitionConfiguration : IEntityTypeConfiguration<PropertyAcquisition>
{
    public void Configure(EntityTypeBuilder<PropertyAcquisition> builder)
    {
        builder.ToTable("property_acquisitions");
        builder.Property(a => a.PurchasePrice).HasPrecision(18, 2);
        builder.Property(a => a.Notary).HasPrecision(18, 2);
        builder.Property(a => a.LandRegistry).HasPrecision(18, 2);
        builder.Property(a => a.TransferTax).HasPrecision(18, 2);
        builder.Property(a => a.Broker).HasPrecision(18, 2);
        builder.Property(a => a.LandSharePercent).HasPrecision(5, 2);
        builder.Property(a => a.RateOverridePercent).HasPrecision(5, 2);
        builder.Property(a => a.SpecialRatePercent).HasPrecision(5, 2);
        builder.Property(a => a.Notes).HasMaxLength(2000);
        builder.Ignore(a => a.AncillaryCosts);
        builder.HasOne<Property>().WithMany().HasForeignKey(a => a.PropertyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(a => a.PropertyId).IsUnique().HasFilter("NOT deleted");
    }
}

internal sealed class PropertyCostConfiguration : IEntityTypeConfiguration<PropertyCost>
{
    public void Configure(EntityTypeBuilder<PropertyCost> builder)
    {
        builder.ToTable("property_costs");
        builder.Property(k => k.Amount).HasPrecision(18, 2);
        builder.Property(k => k.Description).HasMaxLength(200);
        builder.HasOne<Property>().WithMany().HasForeignKey(k => k.PropertyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(k => new { k.PropertyId, k.Date });
    }
}

internal sealed class PropertyAssetConfiguration : IEntityTypeConfiguration<PropertyAsset>
{
    public void Configure(EntityTypeBuilder<PropertyAsset> builder)
    {
        builder.ToTable("property_assets");
        builder.Property(w => w.Name).HasMaxLength(200).UseCollation(MietHahnDbContext.GermanCollation);
        builder.Property(w => w.Cost).HasPrecision(18, 2);
        builder.HasOne<Property>().WithMany().HasForeignKey(w => w.PropertyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(w => new { w.PropertyId, w.AcquisitionDate });
    }
}

internal sealed class LoanConfiguration : IEntityTypeConfiguration<Loan>
{
    public void Configure(EntityTypeBuilder<Loan> builder)
    {
        builder.ToTable("loans");
        builder.Property(d => d.Bank).HasMaxLength(200).UseCollation(MietHahnDbContext.GermanCollation);
        builder.Property(d => d.LoanNumber).HasMaxLength(100);
        builder.Property(d => d.Principal).HasPrecision(18, 2);
        builder.Property(d => d.InterestRatePercent).HasPrecision(7, 4);
        builder.Property(d => d.Instalment).HasPrecision(18, 2);
        builder.Property(d => d.SharePercent).HasPrecision(5, 2);
        builder.Property(d => d.Notes).HasMaxLength(2000);
        builder.HasOne<Property>().WithMany().HasForeignKey(d => d.PropertyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(d => d.PropertyId);
    }
}

internal sealed class LoanExtraPaymentConfiguration : IEntityTypeConfiguration<LoanExtraPayment>
{
    public void Configure(EntityTypeBuilder<LoanExtraPayment> builder)
    {
        builder.ToTable("loan_extra_payments");
        builder.Property(s => s.Amount).HasPrecision(18, 2);
        builder.HasOne<Loan>().WithMany().HasForeignKey(s => s.LoanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(s => new { s.LoanId, s.Date });
    }
}

internal sealed class LoanCertificateConfiguration : IEntityTypeConfiguration<LoanCertificate>
{
    public void Configure(EntityTypeBuilder<LoanCertificate> builder)
    {
        builder.ToTable("loan_certificates");
        builder.Property(b => b.Interest).HasPrecision(18, 2);
        builder.Property(b => b.ClosingBalance).HasPrecision(18, 2);
        builder.HasOne<Loan>().WithMany().HasForeignKey(b => b.LoanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(b => new { b.LoanId, b.Year }).IsUnique().HasFilter("NOT deleted");
    }
}

internal sealed class FinancingCostConfiguration : IEntityTypeConfiguration<FinancingCost>
{
    public void Configure(EntityTypeBuilder<FinancingCost> builder)
    {
        builder.ToTable("financing_costs");
        builder.Property(k => k.Amount).HasPrecision(18, 2);
        builder.Property(k => k.Description).HasMaxLength(200);
        builder.Property(k => k.Kind).HasConversion<string>().HasMaxLength(30);
        builder.HasOne<Property>().WithMany().HasForeignKey(k => k.PropertyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Loan>().WithMany().HasForeignKey(k => k.LoanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(k => new { k.PropertyId, k.Date });
    }
}
