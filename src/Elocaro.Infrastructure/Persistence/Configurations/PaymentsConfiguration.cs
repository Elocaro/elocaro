using Elocaro.Domain.Leases;
using Elocaro.Domain.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elocaro.Infrastructure.Persistence.Configurations;

internal sealed class DirectDebitMandateConfiguration : IEntityTypeConfiguration<DirectDebitMandate>
{
    public void Configure(EntityTypeBuilder<DirectDebitMandate> builder)
    {
        builder.ToTable("direct_debit_mandates");
        builder.Property(m => m.Reference).HasMaxLength(DirectDebitMandate.MaxReference);
        builder.Property(m => m.DebtorName).HasMaxLength(200);
        builder.Property(m => m.DebtorIban).HasMaxLength(34);
        builder.Property(m => m.DebtorBic).HasMaxLength(11);
        builder.Property(m => m.Note).HasMaxLength(500);
        builder.Property(m => m.Sequence).HasConversion<string>().HasMaxLength(20);
        builder.HasOne<Lease>().WithMany().HasForeignKey(m => m.LeaseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(m => m.LeaseId);
        // The reference identifies the mandate towards the bank: unique among the mandates that still exist.
        builder.HasIndex(m => m.Reference).IsUnique().HasFilter("NOT deleted");
    }
}
