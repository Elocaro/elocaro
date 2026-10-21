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
