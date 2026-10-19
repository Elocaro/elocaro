using Quartz;

namespace MietHahn.Infrastructure.Mail;

/// <summary>Stellt fällige Mails aus der Outbox zu (alle 30 Sekunden, nie parallel).</summary>
[DisallowConcurrentExecution]
internal sealed class MailOutboxJob(MailOutbox outbox) : IJob
{
    public static readonly JobKey Key = new("mail-outbox", "system");

    public async Task Execute(IJobExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        await outbox.DeliverAsync(context.CancellationToken);
    }
}
