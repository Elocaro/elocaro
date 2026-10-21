using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MietHahn.Domain.Files;
using MietHahn.Domain.Leases;
using MietHahn.Domain.People;
using MietHahn.Domain.Properties;
using MietHahn.Domain.Receipts;
using MietHahn.Domain.Tickets;

namespace MietHahn.Infrastructure.Persistence.Configurations;

internal sealed class TicketConfiguration : IEntityTypeConfiguration<Ticket>
{
    public void Configure(EntityTypeBuilder<Ticket> builder)
    {
        builder.ToTable("tickets");
        builder.Property(t => t.Title).HasMaxLength(200).UseCollation(MietHahnDbContext.GermanCollation);
        builder.Property(t => t.Description).HasMaxLength(4000);
        builder.Property(t => t.Location).HasMaxLength(200);
        builder.Property(t => t.AccessTimes).HasMaxLength(500);
        builder.Property(t => t.Category).HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.Urgency).HasConversion<string>().HasMaxLength(10);
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);
        builder.Ignore(t => t.IsOpen);
        builder.HasOne<Property>().WithMany().HasForeignKey(t => t.PropertyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Unit>().WithMany().HasForeignKey(t => t.UnitId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Lease>().WithMany().HasForeignKey(t => t.LeaseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Person>().WithMany().HasForeignKey(t => t.ReporterId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Person>().WithMany().HasForeignKey(t => t.AssigneeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Receipt>().WithMany().HasForeignKey(t => t.ReceiptId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(t => t.Status).HasFilter("NOT deleted");
        builder.HasIndex(t => t.AssigneeId).HasFilter("NOT deleted");
    }
}

internal sealed class TicketCommentConfiguration : IEntityTypeConfiguration<TicketComment>
{
    public void Configure(EntityTypeBuilder<TicketComment> builder)
    {
        builder.ToTable("ticket_comments");
        builder.Property(c => c.Text).HasMaxLength(4000);
        builder.Property(c => c.AuthorName).HasMaxLength(200);
        builder.HasOne<Ticket>().WithMany().HasForeignKey(c => c.TicketId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(c => c.TicketId);
    }
}

internal sealed class TicketPhotoConfiguration : IEntityTypeConfiguration<TicketPhoto>
{
    public void Configure(EntityTypeBuilder<TicketPhoto> builder)
    {
        builder.ToTable("ticket_photos");
        builder.HasOne<Ticket>().WithMany().HasForeignKey(p => p.TicketId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TicketComment>().WithMany().HasForeignKey(p => p.CommentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StoredFile>().WithMany().HasForeignKey(p => p.FileId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(p => p.TicketId);
        builder.HasIndex(p => p.FileId);
    }
}

internal sealed class TicketWorkConfiguration : IEntityTypeConfiguration<TicketWork>
{
    public void Configure(EntityTypeBuilder<TicketWork> builder)
    {
        builder.ToTable("ticket_work");
        builder.Property(w => w.Material).HasMaxLength(500);
        builder.Property(w => w.Note).HasMaxLength(1000);
        builder.Property(w => w.AuthorName).HasMaxLength(200);
        builder.HasOne<Ticket>().WithMany().HasForeignKey(w => w.TicketId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(w => w.TicketId);
    }
}

internal sealed class TicketInvoiceConfiguration : IEntityTypeConfiguration<TicketInvoice>
{
    public void Configure(EntityTypeBuilder<TicketInvoice> builder)
    {
        builder.ToTable("ticket_invoices");
        builder.Property(i => i.UploadedBy).HasMaxLength(200);
        builder.HasOne<Ticket>().WithMany().HasForeignKey(i => i.TicketId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StoredFile>().WithMany().HasForeignKey(i => i.FileId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(i => i.TicketId);
        builder.HasIndex(i => i.FileId);
    }
}

internal sealed class TicketAccessLinkConfiguration : IEntityTypeConfiguration<TicketAccessLink>
{
    public void Configure(EntityTypeBuilder<TicketAccessLink> builder)
    {
        builder.ToTable("ticket_access_links");
        builder.Property(l => l.TokenHash).HasMaxLength(64);
        builder.Property(l => l.Recipient).HasMaxLength(200);
        builder.HasOne<Ticket>().WithMany().HasForeignKey(l => l.TicketId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(l => l.TokenHash).IsUnique();
        builder.HasIndex(l => l.TicketId);
    }
}

internal sealed class CareRoutineConfiguration : IEntityTypeConfiguration<CareRoutine>
{
    public void Configure(EntityTypeBuilder<CareRoutine> builder)
    {
        builder.ToTable("care_routines");
        builder.Property(r => r.Title).HasMaxLength(200).UseCollation(MietHahnDbContext.GermanCollation);
        builder.Property(r => r.Description).HasMaxLength(1000);
        builder.Property(r => r.Interval).HasConversion<string>().HasMaxLength(10);
        builder.HasOne<Property>().WithMany().HasForeignKey(r => r.PropertyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(r => r.PropertyId).HasFilter("NOT deleted");
    }
}

internal sealed class CareCheckConfiguration : IEntityTypeConfiguration<CareCheck>
{
    public void Configure(EntityTypeBuilder<CareCheck> builder)
    {
        builder.ToTable("care_checks");
        builder.Property(c => c.DoneBy).HasMaxLength(200);
        builder.Property(c => c.Note).HasMaxLength(1000);
        builder.HasOne<CareRoutine>().WithMany().HasForeignKey(c => c.RoutineId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(c => new { c.RoutineId, c.DoneOn });
    }
}
