using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MietHahn.Application.Communication;
using MietHahn.Domain.Admin;
using MietHahn.Domain.Common;
using MietHahn.Domain.Documents;
using MietHahn.Domain.Leases;
using MietHahn.Domain.People;
using MietHahn.Domain.Properties;
using MietHahn.Infrastructure.Common;
using MietHahn.Infrastructure.Persistence;
using Quartz;

namespace MietHahn.Infrastructure.Portal;

/// <summary>
/// Tells tenants with a portal account by mail that a document was released for them. Each document is announced once
/// (<see cref="Document.TenantsNotifiedAt"/>); the mail names the title only, the document itself stays in the portal.
/// </summary>
internal sealed class DocumentNotifications(MietHahnDbContext db, IMailSender mail, IOptions<ApplicationOptions> app, TimeProvider clock)
{
    /// <returns>Number of mails queued.</returns>
    public async Task<int> SendAsync(CancellationToken cancellationToken = default)
    {
        var documents = await db.Set<Document>().Where(d => d.SharedWithTenants && d.TenantsNotifiedAt == null).OrderBy(d => d.CreatedAt).ToListAsync(cancellationToken);
        if (documents.Count == 0)
        {
            return 0;
        }

        var now = clock.GetUtcNow();
        var today = GermanTime.Today(now);
        var leases = await db.Set<Lease>().AsNoTracking().Include(v => v.Parties).ToListAsync(cancellationToken);
        var units = await db.Set<Unit>().AsNoTracking().ToDictionaryAsync(e => e.Id, e => e.PropertyId, cancellationToken);
        var current = leases.Where(v => v.Term.From <= today && (v.Term.To == null || v.Term.To >= today)).ToList();

        var titles = new Dictionary<Guid, List<string>>();
        foreach (var document in documents)
        {
            var persons = new HashSet<Guid>();
            if (document.PersonId is { } person)
            {
                persons.Add(person);
            }

            if (document.LeaseId is { } leaseId)
            {
                persons.UnionWith(leases.Where(v => v.Id == leaseId).SelectMany(Tenants));
            }

            if (document.UnitId is { } unitId)
            {
                persons.UnionWith(current.Where(v => v.UnitId == unitId).SelectMany(Tenants));
            }

            if (document.PropertyId is { } propertyId)
            {
                persons.UnionWith(current.Where(v => units.GetValueOrDefault(v.UnitId) == propertyId).SelectMany(Tenants));
            }

            foreach (var id in persons)
            {
                if (!titles.TryGetValue(id, out var list))
                {
                    titles[id] = list = [];
                }

                list.Add(document.Title);
            }

            document.MarkTenantsNotified(now);
        }

        var ids = titles.Keys.ToList();
        var recipients = await db.Set<Person>().AsNoTracking().Where(p => ids.Contains(p.Id) && p.UserId != null && p.Email != null && p.AnonymizedAt == null).ToListAsync(cancellationToken);
        var sender = (await db.Set<LandlordProfile>().AsNoTracking().OrderBy(p => p.CreatedAt).FirstOrDefaultAsync(cancellationToken))?.Name ?? "Ihre Hausverwaltung";
        var link = app.Value.Link("/portal/dokumente").ToString();
        foreach (var recipient in recipients)
        {
            await mail.EnqueueAsync(new MailRequest(
                [recipient.Email!],
                "document-shared",
                new { Salutation = LetterParts.SalutationOf([recipient]), Titles = titles[recipient.Id].Distinct().ToList(), Link = link, SenderName = sender },
                Reference: "document-shared"), cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        return recipients.Count;
    }

    private static IEnumerable<Guid> Tenants(Lease lease) => lease.Tenant.Select(p => p.PersonId);
}

[DisallowConcurrentExecution]
internal sealed class DocumentNotificationJob(DocumentNotifications notifications) : IJob
{
    public static readonly JobKey Key = new("document-notifications", "portal");

    public async Task Execute(IJobExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        await notifications.SendAsync(context.CancellationToken);
    }
}
