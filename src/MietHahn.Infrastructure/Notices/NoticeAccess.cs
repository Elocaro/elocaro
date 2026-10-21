using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MietHahn.Application.Files;
using MietHahn.Application.Notices;
using MietHahn.Application.Todos;
using MietHahn.Domain.Common;
using MietHahn.Domain.Leases;
using MietHahn.Domain.Notices;
using MietHahn.Infrastructure.Persistence;

namespace MietHahn.Infrastructure.Notices;

/// <summary>Tenants may download the attachments of current notices of their property and the files of their own message threads.</summary>
internal sealed class NoticeFileShare(MietHahnDbContext db, TimeProvider clock) : IFileShare
{
    public async Task<bool> IsSharedAsync(Guid fileId, ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        var (viewer, persons) = await TenantScope.ViewerAsync(db, user, cancellationToken);
        if (viewer != NoticeViewer.Tenant)
        {
            return false;
        }

        var today = GermanTime.Today(clock.GetUtcNow());
        var leases = (await TenantScope.CurrentLeasesAsync(db, today, cancellationToken)).Where(v => v.Tenant.Any(p => persons.Contains(p.PersonId))).ToList();
        var units = await TenantScope.PropertyOfUnitAsync(db, cancellationToken);
        var ownUnits = leases.Select(v => (Property: units.GetValueOrDefault(v.UnitId), Unit: v.UnitId)).ToList();
        var properties = ownUnits.Select(x => x.Property).Distinct().ToList();
        var noticeIds = await db.Set<NoticeFile>().AsNoTracking().Where(f => f.FileId == fileId).Select(f => f.NoticeId).ToListAsync(cancellationToken);
        if (noticeIds.Count > 0)
        {
            var notices = await db.Set<Notice>().AsNoTracking().Where(n => noticeIds.Contains(n.Id) && properties.Contains(n.PropertyId)).ToListAsync(cancellationToken);
            if (notices.Any(n => n.IsCurrent(today) && ownUnits.Any(x => TenantScope.Reaches(n, x.Property, x.Unit))))
            {
                return true;
            }
        }

        var own = await db.Set<Lease>().AsNoTracking().Where(v => v.Parties.Any(p => persons.Contains(p.PersonId))).Select(v => v.Id).ToListAsync(cancellationToken);
        var threads = db.Set<MessageThread>().Where(t => own.Contains(t.LeaseId)).Select(t => t.Id);
        return await db.Set<MessageFile>().AnyAsync(f => f.FileId == fileId && threads.Contains(f.ThreadId), cancellationToken);
    }
}

/// <summary>Dashboard: unanswered tenant messages.</summary>
internal sealed class MessageTodos(MietHahnDbContext db) : ITodoSource
{
    public async Task<IReadOnlyList<TodoItem>> TodosAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!TenantScope.IsAdmin(user))
        {
            return [];
        }

        var threads = await db.Set<MessageThread>().AsNoTracking().Where(t => t.AdminUnread).OrderBy(t => t.LastMessageAt).ToListAsync(cancellationToken);
        return threads.Select(t => new TodoItem("Nachrichten", $"Neue Nachricht: {t.Subject}", $"/verwaltung/nachrichten/{t.Id}", Urgency.Soon, "Lesen und beantworten.", null)).ToList();
    }
}

/// <summary>Dashboard: notices that must be read and that some tenants have not confirmed a week after they started.</summary>
internal sealed class NoticeAcknowledgementTodos(INotices notices, TimeProvider clock) : ITodoSource
{
    public const int GraceDays = 7;

    public async Task<IReadOnlyList<TodoItem>> TodosAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!TenantScope.IsAdmin(user))
        {
            return [];
        }

        var today = GermanTime.Today(clock.GetUtcNow());
        return (await notices.ListAsync(user, cancellationToken))
            .Where(n => n.IsCurrent && n.RequiresAcknowledgement && n.Read < n.Recipients && n.ValidFrom.AddDays(GraceDays) <= today)
            .OrderBy(n => n.ValidFrom)
            .Select(n => new TodoItem("Aushänge", $"Pflicht-Lesen offen: {n.Title} ({n.Read} von {n.Recipients} bestätigt)", $"/verwaltung/aushaenge/{n.Id}", Urgency.Soon,
                "Mieter erinnern oder den Aushang im Treppenhaus aufhängen.", n.ValidUntil))
            .ToList();
    }
}
