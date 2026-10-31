using Elocaro.Domain.Communication;
using Elocaro.Domain.Finance;
using Elocaro.Domain.Statements;
using Elocaro.Infrastructure.Mail;
using Elocaro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Quartz;

namespace Elocaro.Infrastructure.Operations;

/// <summary>
/// Daily: clears delivered mails of secret templates (also those of templates that became secret later) and, after the retention
/// period, deletes mails physically. A mail that a statement dispatch or dunning refers to stays as proof, only without its content.
/// Waiting mails are never touched, they need their content for delivery.
/// </summary>
internal sealed class MailRetention(ElocaroDbContext db, TimeProvider clock, IOptions<MailOptions> options)
{
    public async Task CleanUpAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var mails = db.Set<OutgoingMail>().IgnoreQueryFilters().Where(m => m.Status != MailStatus.Pending && m.ContentPurgedAt == null);
        var secret = OutgoingMail.SecretTemplates;
        await Purge(mails.Where(m => m.Template != null && secret.Contains(m.Template)), now, cancellationToken);

        if (options.Value.RetentionDays <= 0)
        {
            return;
        }

        var cutoff = now.AddDays(-options.Value.RetentionDays);
        var expired = db.Set<OutgoingMail>().IgnoreQueryFilters().Where(m => m.Status != MailStatus.Pending && (m.SentAt ?? m.CreatedAt) < cutoff);
        var dispatches = db.Set<StatementDispatch>().IgnoreQueryFilters().Where(d => d.MailId != null).Select(d => d.MailId);
        var dunnings = db.Set<Dunning>().IgnoreQueryFilters().Where(d => d.MailId != null).Select(d => d.MailId);

        await Purge(expired.Where(m => m.ContentPurgedAt == null && (dispatches.Contains((Guid?)m.Id) || dunnings.Contains((Guid?)m.Id))), now, cancellationToken);
        await expired.Where(m => !dispatches.Contains((Guid?)m.Id) && !dunnings.Contains((Guid?)m.Id)).ExecuteDeleteAsync(cancellationToken);
    }

    private static Task<int> Purge(IQueryable<OutgoingMail> mails, DateTimeOffset now, CancellationToken cancellationToken) =>
        mails.ExecuteUpdateAsync(
            s => s.SetProperty(m => m.Html, OutgoingMail.PurgedContent)
                .SetProperty(m => m.Text, OutgoingMail.PurgedContent)
                .SetProperty(m => m.ContentPurgedAt, now),
            cancellationToken);
}

[DisallowConcurrentExecution]
internal sealed class MailRetentionJob(MailRetention retention) : IJob
{
    public static readonly JobKey Key = new("mail-retention", "system");

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        await retention.CleanUpAsync(cancellationToken);
    }
}
