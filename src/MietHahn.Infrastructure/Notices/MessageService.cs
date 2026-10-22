using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MietHahn.Application.Common;
using MietHahn.Application.Communication;
using MietHahn.Application.Notices;
using MietHahn.Domain.Admin;
using MietHahn.Domain.Common;
using MietHahn.Domain.Leases;
using MietHahn.Domain.Notices;
using MietHahn.Domain.People;
using MietHahn.Domain.Properties;
using MietHahn.Infrastructure.Common;
using MietHahn.Infrastructure.Identity;
using MietHahn.Infrastructure.Leases;
using MietHahn.Infrastructure.Persistence;

namespace MietHahn.Infrastructure.Notices;

internal sealed class MessageService(MietHahnDbContext db, IMailSender mail, IOptions<ApplicationOptions> app, TimeProvider clock) : IMessages
{
    private DateOnly Today => GermanTime.Today(clock.GetUtcNow());

    public async Task<IReadOnlyList<ThreadRow>> ListAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        var (viewer, persons) = await TenantScope.ViewerAsync(db, user, cancellationToken);
        var threads = await VisibleThreads(viewer, await OwnLeaseIdsAsync(viewer, persons, cancellationToken)).AsNoTracking().OrderByDescending(t => t.LastMessageAt).ToListAsync(cancellationToken);
        return await RowsAsync(threads, viewer, cancellationToken);
    }

    public async Task<ThreadView?> GetAsync(ClaimsPrincipal user, Guid id, CancellationToken cancellationToken = default)
    {
        var (viewer, persons) = await TenantScope.ViewerAsync(db, user, cancellationToken);
        var thread = await VisibleThreads(viewer, await OwnLeaseIdsAsync(viewer, persons, cancellationToken)).SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (thread is null)
        {
            return null;
        }

        var wasUnread = viewer == NoticeViewer.Tenant ? thread.TenantUnread : thread.AdminUnread;
        var view = (await ViewsAsync([thread], viewer, cancellationToken))[0];
        if (wasUnread)
        {
            thread.MarkRead(viewer == NoticeViewer.Tenant);
            await db.SaveChangesAsync(cancellationToken);
        }

        return view with { Row = view.Row with { Unread = false } };
    }

    public async Task<IReadOnlyList<MessageTarget>> TargetsAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        var (viewer, persons) = await TenantScope.ViewerAsync(db, user, cancellationToken);
        if (viewer != NoticeViewer.Tenant)
        {
            return [];
        }

        var leases = (await TenantScope.CurrentLeasesAsync(db, Today, cancellationToken)).Where(v => v.Tenant.Any(p => persons.Contains(p.PersonId))).OrderBy(v => v.Start).ToList();
        var units = await RentalUnit.GetAsync(db, cancellationToken);
        return leases.Select(v => new MessageTarget(v.Id, units.GetValueOrDefault(v.RentalUnitId)?.Name ?? "", units.GetValueOrDefault(v.RentalUnitId)?.Property ?? "", "")).ToList();
    }

    public Task<Result> StartAsync(ClaimsPrincipal user, NewThread input, IReadOnlyList<Guid> fileIds, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(input);
            var (viewer, persons) = await TenantScope.ViewerAsync(db, user, cancellationToken);
            Guid leaseId;
            if (viewer == NoticeViewer.Tenant)
            {
                var targets = await TargetsAsync(user, cancellationToken);
                leaseId = (input.LeaseId is { } wanted ? targets.FirstOrDefault(t => t.LeaseId == wanted) : targets.Count > 0 ? targets[0] : null)?.LeaseId
                    ?? throw new RuleViolationException("Ihrem Konto ist kein laufendes Mietverhältnis zugeordnet.");
            }
            else if (viewer == NoticeViewer.Admin)
            {
                leaseId = input.LeaseId ?? throw new RuleViolationException("Bitte das Mietverhältnis wählen.");
                if (!await db.Set<Lease>().AnyAsync(v => v.Id == leaseId, cancellationToken))
                {
                    throw new RuleViolationException("Das Mietverhältnis wurde nicht gefunden.");
                }
            }
            else
            {
                throw new RuleViolationException("Dafür fehlt die Berechtigung.");
            }

            var thread = new MessageThread(leaseId, input.Subject, clock.GetUtcNow());
            db.Add(thread);
            var message = await AddMessageAsync(user, thread, viewer, input.Text, fileIds, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await NotifyAsync(thread, viewer, cancellationToken);
            _ = message;
            return thread.Id;
        });

    public Task<Result> ReplyAsync(ClaimsPrincipal user, Guid id, string text, IReadOnlyList<Guid> fileIds, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var (viewer, persons) = await TenantScope.ViewerAsync(db, user, cancellationToken);
            var thread = await VisibleThreads(viewer, await OwnLeaseIdsAsync(viewer, persons, cancellationToken)).SingleOrDefaultAsync(t => t.Id == id, cancellationToken) ?? throw new NotFoundException();
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new RuleViolationException("Bitte eine Nachricht schreiben.");
            }

            await AddMessageAsync(user, thread, viewer, text, fileIds, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await NotifyAsync(thread, viewer, cancellationToken);
            return id;
        });

    public async Task<IReadOnlyList<ThreadView>> ForLeaseAsync(ClaimsPrincipal user, Guid leaseId, CancellationToken cancellationToken = default)
    {
        var (viewer, _) = await TenantScope.ViewerAsync(db, user, cancellationToken);
        if (viewer != NoticeViewer.Admin)
        {
            return [];
        }

        var threads = await db.Set<MessageThread>().Where(t => t.LeaseId == leaseId).OrderByDescending(t => t.LastMessageAt).ToListAsync(cancellationToken);
        return await ViewsAsync(threads, viewer, cancellationToken);
    }

    private async Task<Message> AddMessageAsync(ClaimsPrincipal user, MessageThread thread, NoticeViewer viewer, string text, IReadOnlyList<Guid> fileIds, CancellationToken cancellationToken)
    {
        if (fileIds.Count > Message.MaxAttachments)
        {
            throw new RuleViolationException($"Höchstens {Message.MaxAttachments} Anhänge je Nachricht.");
        }

        var userId = TenantScope.UserId(user);
        var name = userId is { } id ? await db.Set<ApplicationUser>().Where(u => u.Id == id).Select(u => u.DisplayName).FirstOrDefaultAsync(cancellationToken) ?? "Unbekannt" : "Unbekannt";
        var message = new Message(thread.Id, text, viewer == NoticeViewer.Tenant, userId, name);
        db.Add(message);
        foreach (var fileId in fileIds)
        {
            db.Add(new MessageFile(thread.Id, message.Id, fileId));
        }

        thread.Add(viewer == NoticeViewer.Tenant, clock.GetUtcNow());
        return message;
    }

    /// <summary>Points to the portal or the administration; the mail never contains the text.</summary>
    private async Task NotifyAsync(MessageThread thread, NoticeViewer actor, CancellationToken cancellationToken)
    {
        if (actor == NoticeViewer.Tenant)
        {
            var admins = await AdminRecipients.GetAsync(db, cancellationToken);
            if (admins.Count > 0)
            {
                await mail.EnqueueAsync(new MailRequest(admins, "message-update", new
                {
                    Salutation = "Hallo,",
                    Message = "ein Mieter hat Ihnen im Portal eine Nachricht geschrieben.",
                    Link = app.Value.Link($"/verwaltung/nachrichten/{thread.Id}").ToString(),
                    SenderName = "MietHahn",
                }, Reference: $"message:{thread.Id}"), cancellationToken);
            }

            return;
        }

        var lease = await db.Set<Lease>().AsNoTracking().Include(v => v.Parties).SingleAsync(v => v.Id == thread.LeaseId, cancellationToken);
        var sender = (await Admin.LandlordProfiles.ForUnitAsync(db, lease.UnitId, cancellationToken))?.Name ?? "MietHahn";
        var ids = lease.Tenant.Select(p => p.PersonId).ToList();
        var recipients = await db.Set<Person>().AsNoTracking().Where(p => ids.Contains(p.Id) && p.UserId != null && p.Email != null && p.AnonymizedAt == null).ToListAsync(cancellationToken);
        foreach (var person in recipients)
        {
            await mail.EnqueueAsync(new MailRequest([person.Email!], "message-update", new
            {
                Salutation = LetterParts.SalutationOf([person]),
                Message = "Sie haben im Mieterportal eine neue Nachricht von Ihrer Verwaltung.",
                Link = app.Value.Link($"/portal/nachrichten/{thread.Id}").ToString(),
                SenderName = sender,
            }, Reference: $"message:{thread.Id}"), cancellationToken);
        }
    }

    private async Task<List<Guid>> OwnLeaseIdsAsync(NoticeViewer viewer, List<Guid> persons, CancellationToken cancellationToken) =>
        viewer == NoticeViewer.Tenant
            ? await db.Set<Lease>().AsNoTracking().Where(v => v.Parties.Any(p => persons.Contains(p.PersonId) && p.Role == PartyRole.Tenant)).Select(v => v.Id).ToListAsync(cancellationToken)
            : [];

    private IQueryable<MessageThread> VisibleThreads(NoticeViewer viewer, List<Guid> leaseIds) => viewer switch
    {
        NoticeViewer.Admin => db.Set<MessageThread>(),
        NoticeViewer.Tenant => db.Set<MessageThread>().Where(t => leaseIds.Contains(t.LeaseId)),
        _ => db.Set<MessageThread>().Where(_ => false),
    };

    private async Task<List<ThreadRow>> RowsAsync(List<MessageThread> threads, NoticeViewer viewer, CancellationToken cancellationToken)
    {
        if (threads.Count == 0)
        {
            return [];
        }

        var leaseIds = threads.Select(t => t.LeaseId).Distinct().ToList();
        var leases = await db.Set<Lease>().AsNoTracking().Include(v => v.Parties).Where(v => leaseIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, cancellationToken);
        var units = await RentalUnit.GetAsync(db, cancellationToken);
        var personIds = leases.Values.SelectMany(v => v.Tenant.Select(p => p.PersonId)).Distinct().ToList();
        var names = await db.Set<Person>().AsNoTracking().Where(p => personIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.DisplayName, cancellationToken);
        return threads.Select(t =>
        {
            var lease = leases[t.LeaseId];
            var unit = units.GetValueOrDefault(lease.RentalUnitId);
            return new ThreadRow(t.Id, t.LeaseId, t.Subject, unit?.Name ?? "", unit?.Property ?? "", string.Join(", ", lease.Tenant.Select(p => names.GetValueOrDefault(p.PersonId, "?"))),
                t.LastMessageAt, viewer == NoticeViewer.Tenant ? t.TenantUnread : t.AdminUnread);
        }).ToList();
    }

    private async Task<List<ThreadView>> ViewsAsync(List<MessageThread> threads, NoticeViewer viewer, CancellationToken cancellationToken)
    {
        var rows = await RowsAsync(threads, viewer, cancellationToken);
        var ids = threads.Select(t => t.Id).ToList();
        var messages = (await db.Set<Message>().AsNoTracking().Where(m => ids.Contains(m.ThreadId)).OrderBy(m => m.CreatedAt).ToListAsync(cancellationToken)).ToLookup(m => m.ThreadId);
        var files = (await db.Set<MessageFile>().AsNoTracking().Where(f => ids.Contains(f.ThreadId)).OrderBy(f => f.CreatedAt).ToListAsync(cancellationToken)).ToLookup(f => f.MessageId, f => f.FileId);
        return rows.Select(r => new ThreadView(r, messages[r.Id].Select(m => new MessageView(m.Id, m.AuthorName, m.FromTenant, m.Text, m.CreatedAt, files[m.Id].ToList())).ToList())).ToList();
    }
}
