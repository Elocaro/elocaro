using Elocaro.Domain.Communication;
using Elocaro.Domain.Documents;
using Elocaro.Domain.Files;
using Elocaro.Domain.Finance;
using Elocaro.Domain.Leases;
using Elocaro.Domain.Properties;
using Elocaro.Domain.Statements;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elocaro.Infrastructure.Persistence.Configurations;

internal sealed class StatementDispatchConfiguration : IEntityTypeConfiguration<StatementDispatch>
{
    public void Configure(EntityTypeBuilder<StatementDispatch> builder)
    {
        builder.ToTable("statement_dispatches");
        builder.Property(d => d.Channel).HasConversion<string>().HasMaxLength(20);
        builder.Property(d => d.Balance).HasPrecision(18, 2);
        builder.Property(d => d.BookedAmount).HasPrecision(18, 2);
        builder.Property(d => d.Note).HasMaxLength(500);
        builder.Property(d => d.ReceiptNote).HasMaxLength(500);
        builder.HasOne<BillingStatement>().WithMany().HasForeignKey(d => d.StatementId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Lease>().WithMany().HasForeignKey(d => d.LeaseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StoredFile>().WithMany().HasForeignKey(d => d.FileId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Document>().WithMany().HasForeignKey(d => d.DocumentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OutgoingMail>().WithMany().HasForeignKey(d => d.MailId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Charge>().WithMany().HasForeignKey(d => d.ChargeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(d => new { d.StatementId, d.LeaseId }).IsUnique().HasFilter("NOT deleted");
        builder.HasIndex(d => d.LeaseId);
    }
}

internal sealed class PrepaymentProposalConfiguration : IEntityTypeConfiguration<PrepaymentProposal>
{
    public void Configure(EntityTypeBuilder<PrepaymentProposal> builder)
    {
        builder.ToTable("prepayment_proposals");
        builder.Property(p => p.Component).HasConversion<string>().HasMaxLength(30);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.Current).HasPrecision(18, 2);
        builder.Property(p => p.Proposed).HasPrecision(18, 2);
        builder.HasOne<StatementDispatch>().WithMany().HasForeignKey(p => p.DispatchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Lease>().WithMany().HasForeignKey(p => p.LeaseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(p => p.DispatchId);
    }
}

internal sealed class StatementReminderDismissalConfiguration : IEntityTypeConfiguration<StatementReminderDismissal>
{
    public void Configure(EntityTypeBuilder<StatementReminderDismissal> builder)
    {
        builder.ToTable("statement_reminder_dismissals");
        builder.HasOne<Property>().WithMany().HasForeignKey(d => d.PropertyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(d => new { d.PropertyId, d.Year }).IsUnique().HasFilter("NOT deleted");
    }
}

internal sealed class StatementReceiptShareConfiguration : IEntityTypeConfiguration<StatementReceiptShare>
{
    public void Configure(EntityTypeBuilder<StatementReceiptShare> builder)
    {
        builder.ToTable("statement_receipt_shares");
        builder.HasOne<BillingStatement>().WithMany().HasForeignKey(s => s.StatementId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Elocaro.Domain.Receipts.Receipt>().WithMany().HasForeignKey(s => s.ReceiptId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(s => new { s.StatementId, s.ReceiptId }).IsUnique().HasFilter("NOT deleted");
        builder.HasIndex(s => s.ReceiptId);
    }
}
