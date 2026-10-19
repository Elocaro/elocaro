using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MietHahn.Domain.Communication;

namespace MietHahn.Infrastructure.Persistence.Configurations;

internal sealed class OutgoingMailConfiguration : IEntityTypeConfiguration<OutgoingMail>
{
    public void Configure(EntityTypeBuilder<OutgoingMail> builder)
    {
        builder.ToTable("outgoing_mails");
        builder.Property(m => m.Subject).HasMaxLength(500);
        builder.Property(m => m.Template).HasMaxLength(100);
        builder.Property(m => m.Reference).HasMaxLength(200);
        builder.Property(m => m.LastError).HasMaxLength(OutgoingMail.MaxErrorLength);
        builder.Property(m => m.Status).HasConversion<string>().HasMaxLength(20);

        // For the outbox job: pending mails only, sorted by due date.
        builder.HasIndex(m => m.NextAttemptAt).HasFilter("status = 'Pending'");
        builder.HasIndex(m => m.Reference);
    }
}
