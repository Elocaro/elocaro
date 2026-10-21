using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MietHahn.Domain.Deadlines;
using MietHahn.Domain.Documents;
using MietHahn.Domain.Properties;

namespace MietHahn.Infrastructure.Persistence.Configurations;

internal sealed class RecurringTaskConfiguration : IEntityTypeConfiguration<RecurringTask>
{
    public void Configure(EntityTypeBuilder<RecurringTask> builder)
    {
        builder.ToTable("recurring_tasks");
        builder.Property(t => t.Title).HasMaxLength(200).UseCollation(MietHahnDbContext.GermanCollation);
        builder.Property(t => t.Kind).HasConversion<string>().HasMaxLength(30);
        builder.Property(t => t.Note).HasMaxLength(2000);
        builder.HasOne<Property>().WithMany().HasForeignKey(t => t.PropertyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(t => t.DueOn).HasFilter("active AND NOT deleted");
    }
}

internal sealed class TaskCompletionConfiguration : IEntityTypeConfiguration<TaskCompletion>
{
    public void Configure(EntityTypeBuilder<TaskCompletion> builder)
    {
        builder.ToTable("task_completions");
        builder.Property(c => c.Note).HasMaxLength(2000);
        builder.HasOne<RecurringTask>().WithMany().HasForeignKey(c => c.TaskId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Document>().WithMany().HasForeignKey(c => c.DocumentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(c => new { c.TaskId, c.DoneOn });
    }
}

internal sealed class DeadlineSettingsConfiguration : IEntityTypeConfiguration<DeadlineSettings>
{
    public void Configure(EntityTypeBuilder<DeadlineSettings> builder)
    {
        builder.ToTable("deadline_settings");
        builder.Property(s => s.DefaultLeadDays).HasDefaultValue(DeadlineSettings.DefaultLead);
        builder.Property(s => s.SendReminders).HasDefaultValue(true);
        builder.Property(s => s.SendWeeklySummary).HasDefaultValue(true);
        builder.Property(s => s.CalendarToken).HasMaxLength(100);
    }
}

internal sealed class DeadlineReminderConfiguration : IEntityTypeConfiguration<DeadlineReminder>
{
    public void Configure(EntityTypeBuilder<DeadlineReminder> builder)
    {
        builder.ToTable("deadline_reminders");
        builder.Property(r => r.Key).HasMaxLength(200);
        builder.HasIndex(r => new { r.Key, r.Due }).IsUnique().HasFilter("NOT deleted");
    }
}
