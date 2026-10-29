using Quartz;

namespace Elocaro.Infrastructure.Mail;

/// <summary>Delivers due mails from the outbox (every 30 seconds, never in parallel).</summary>
[DisallowConcurrentExecution]
internal sealed class MailOutboxJob(MailOutbox outbox) : IJob
{
    public static readonly JobKey Key = new("mail-outbox", "system");

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        await outbox.DeliverAsync(cancellationToken);
    }
}
