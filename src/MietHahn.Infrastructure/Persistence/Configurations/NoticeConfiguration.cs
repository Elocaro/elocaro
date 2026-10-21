using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MietHahn.Domain.Files;
using MietHahn.Domain.Leases;
using MietHahn.Domain.Notices;
using MietHahn.Domain.Properties;

namespace MietHahn.Infrastructure.Persistence.Configurations;

internal sealed class NoticeConfiguration : IEntityTypeConfiguration<Notice>
{
    public void Configure(EntityTypeBuilder<Notice> builder)
    {
        builder.ToTable("notices");
        builder.Property(n => n.Title).HasMaxLength(200).UseCollation(MietHahnDbContext.GermanCollation);
        builder.Property(n => n.Body).HasMaxLength(4000);
        builder.Property(n => n.UnitIds).HasColumnType("uuid[]");
        builder.HasOne<Property>().WithMany().HasForeignKey(n => n.PropertyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(n => n.PropertyId).HasFilter("NOT deleted");
    }
}

internal sealed class NoticeFileConfiguration : IEntityTypeConfiguration<NoticeFile>
{
    public void Configure(EntityTypeBuilder<NoticeFile> builder)
    {
        builder.ToTable("notice_files");
        builder.HasOne<Notice>().WithMany().HasForeignKey(f => f.NoticeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StoredFile>().WithMany().HasForeignKey(f => f.FileId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(f => f.NoticeId);
        builder.HasIndex(f => f.FileId);
    }
}

internal sealed class NoticeReadConfiguration : IEntityTypeConfiguration<NoticeRead>
{
    public void Configure(EntityTypeBuilder<NoticeRead> builder)
    {
        builder.ToTable("notice_reads");
        builder.HasOne<Notice>().WithMany().HasForeignKey(l => l.NoticeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(l => new { l.NoticeId, l.UserId }).IsUnique().HasFilter("NOT deleted");
    }
}

internal sealed class MessageThreadConfiguration : IEntityTypeConfiguration<MessageThread>
{
    public void Configure(EntityTypeBuilder<MessageThread> builder)
    {
        builder.ToTable("message_threads");
        builder.Property(t => t.Subject).HasMaxLength(200).UseCollation(MietHahnDbContext.GermanCollation);
        builder.HasOne<Lease>().WithMany().HasForeignKey(t => t.LeaseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(t => t.LeaseId).HasFilter("NOT deleted");
    }
}

internal sealed class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.ToTable("messages");
        builder.Property(m => m.Text).HasMaxLength(4000);
        builder.Property(m => m.AuthorName).HasMaxLength(200);
        builder.HasOne<MessageThread>().WithMany().HasForeignKey(m => m.ThreadId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(m => m.ThreadId);
    }
}

internal sealed class MessageFileConfiguration : IEntityTypeConfiguration<MessageFile>
{
    public void Configure(EntityTypeBuilder<MessageFile> builder)
    {
        builder.ToTable("message_files");
        builder.HasOne<MessageThread>().WithMany().HasForeignKey(f => f.ThreadId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Message>().WithMany().HasForeignKey(f => f.MessageId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StoredFile>().WithMany().HasForeignKey(f => f.FileId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(f => f.ThreadId);
        builder.HasIndex(f => f.FileId);
    }
}
