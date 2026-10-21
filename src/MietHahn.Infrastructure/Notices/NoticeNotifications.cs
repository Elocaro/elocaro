using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MietHahn.Application.Communication;
using MietHahn.Domain.Admin;
using MietHahn.Domain.Common;
using MietHahn.Domain.Notices;
using MietHahn.Domain.People;
using MietHahn.Infrastructure.Common;
using MietHahn.Infrastructure.Persistence;
using Quartz;

namespace MietHahn.Infrastructure.Notices;

/// <summary>
/// Tells the tenants with a portal account by mail about a new notice of their property (each notice once,
/// <see cref="Notice.TenantsNotifiedAt"/>). Expired notices are not announced any more.
/// </summary>
internal sealed class NoticeNotifications(MietHahnDbContext db, IMailSender mail, IOptions<ApplicationOptions> app, TimeProvider clock)
{
    /// <returns>Number of mails queued.</returns>
    public async Task<int> SendAsync(CancellationToken cancellationToken = default) =>
        await SendNewAsync(cancellationToken) + await SendWithdrawnAsync(cancellationToken);

    /// <summary>Tenants who were told about a notice hear when it is withdrawn early.</summary>
    private async Task<int> SendWithdrawnAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var today = GermanTime.Today(now);
        var notices = await db.Set<Notice>().Where(n => n.WithdrawnAt != null && n.WithdrawalNotifiedAt == null).OrderBy(n => n.WithdrawnAt).ToListAsync(cancellationToken);
        if (notices.Count == 0)
        {
            return 0;
        }

        var leases = await TenantScope.CurrentLeasesAsync(db, today, cancellationToken);
        var units = await TenantScope.PropertyOfUnitAsync(db, cancellationToken);
        var titles = new Dictionary<Guid, List<string>>();
        foreach (var notice in notices)
        {
            foreach (var id in leases.Where(v => units.GetValueOrDefault(v.UnitId) == notice.PropertyId).SelectMany(v => v.Tenant.Select(p => p.PersonId)).Distinct())
            {
                if (!titles.TryGetValue(id, out var list))
                {
                    titles[id] = list = [];
                }

                list.Add(notice.Title);
            }

            notice.MarkWithdrawalNotified(now);
        }

        var ids = titles.Keys.ToList();
        var recipients = await db.Set<Person>().AsNoTracking().Where(p => ids.Contains(p.Id) && p.UserId != null && p.Email != null && p.AnonymizedAt == null).ToListAsync(cancellationToken);
        var sender = (await db.Set<LandlordProfile>().AsNoTracking().OrderBy(p => p.CreatedAt).FirstOrDefaultAsync(cancellationToken))?.Name ?? "Ihre Hausverwaltung";
        var link = app.Value.Link("/portal").ToString();
        foreach (var recipient in recipients)
        {
            await mail.EnqueueAsync(new MailRequest(
                [recipient.Email!],
                "notice-withdrawn",
                new { Salutation = LetterParts.SalutationOf([recipient]), Titles = titles[recipient.Id].Distinct().ToList(), Link = link, SenderName = sender },
                Reference: "notice"), cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        return recipients.Count;
    }

    private async Task<int> SendNewAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var today = GermanTime.Today(now);
        var notices = (await db.Set<Notice>().Where(n => n.TenantsNotifiedAt == null).OrderBy(n => n.CreatedAt).ToListAsync(cancellationToken))
            .Where(n => n.ValidFrom <= today).ToList();
        if (notices.Count == 0)
        {
            return 0;
        }

        var leases = await TenantScope.CurrentLeasesAsync(db, today, cancellationToken);
        var units = await TenantScope.PropertyOfUnitAsync(db, cancellationToken);
        var titles = new Dictionary<Guid, List<string>>();
        foreach (var notice in notices)
        {
            if (notice.IsCurrent(today))
            {
                foreach (var id in leases.Where(v => units.GetValueOrDefault(v.UnitId) == notice.PropertyId).SelectMany(v => v.Tenant.Select(p => p.PersonId)).Distinct())
                {
                    if (!titles.TryGetValue(id, out var list))
                    {
                        titles[id] = list = [];
                    }

                    list.Add(notice.Title);
                }
            }

            notice.MarkTenantsNotified(now);
        }

        var ids = titles.Keys.ToList();
        var recipients = await db.Set<Person>().AsNoTracking().Where(p => ids.Contains(p.Id) && p.UserId != null && p.Email != null && p.AnonymizedAt == null).ToListAsync(cancellationToken);
        var sender = (await db.Set<LandlordProfile>().AsNoTracking().OrderBy(p => p.CreatedAt).FirstOrDefaultAsync(cancellationToken))?.Name ?? "Ihre Hausverwaltung";
        var link = app.Value.Link("/portal").ToString();
        foreach (var recipient in recipients)
        {
            await mail.EnqueueAsync(new MailRequest(
                [recipient.Email!],
                "notice",
                new { Salutation = LetterParts.SalutationOf([recipient]), Titles = titles[recipient.Id].Distinct().ToList(), Link = link, SenderName = sender },
                Reference: "notice"), cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        return recipients.Count;
    }
}

[DisallowConcurrentExecution]
internal sealed class NoticeNotificationJob(NoticeNotifications notifications) : IJob
{
    public static readonly JobKey Key = new("notice-notifications", "portal");

    public async Task Execute(IJobExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        await notifications.SendAsync(context.CancellationToken);
    }
}
