using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MietHahn.Application.Accounts;
using MietHahn.Application.Files;
using MietHahn.Application.Todos;
using MietHahn.Domain.Common;
using MietHahn.Domain.People;
using MietHahn.Domain.Tickets;
using MietHahn.Infrastructure.Persistence;

namespace MietHahn.Infrastructure.Tickets;

/// <summary>Tenants and assignees may download the photos of their tickets, except those of internal comments.</summary>
internal sealed class TicketFileShare(MietHahnDbContext db) : IFileShare
{
    public async Task<bool> IsSharedAsync(Guid fileId, ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return false;
        }

        var persons = db.Set<Person>().Where(p => p.UserId == userId).Select(p => p.Id);
        var tenant = user.IsInRole(Roles.Tenant);
        var service = user.IsInRole(Roles.Caretaker) || user.IsInRole(Roles.Tradesperson);
        if (!tenant && !service)
        {
            return false;
        }

        var tickets = db.Set<Ticket>().Where(t => tenant
            ? t.ReporterId != null && persons.Contains(t.ReporterId.Value)
            : t.AssigneeId != null && persons.Contains(t.AssigneeId.Value));
        var internalComments = db.Set<TicketComment>().Where(c => c.IsInternal).Select(c => c.Id);
        return await db.Set<TicketPhoto>().AnyAsync(
            p => p.FileId == fileId && tickets.Select(t => t.Id).Contains(p.TicketId) && (p.CommentId == null || !internalComments.Contains(p.CommentId.Value)),
            cancellationToken);
    }
}

/// <summary>Dashboard: new tickets and tickets with a due appointment.</summary>
internal sealed class TicketTodos(MietHahnDbContext db) : ITodoSource
{
    public async Task<IReadOnlyList<TodoItem>> TodosAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!user.IsInRole(Roles.Admin) && !user.HasClaim(Permissions.ClaimType, nameof(AccessRight.MasterData)))
        {
            return [];
        }

        var open = await db.Set<Ticket>().AsNoTracking().Where(t => t.Status == TicketStatus.New).OrderByDescending(t => t.Urgency).ThenBy(t => t.ReportedAt).ToListAsync(cancellationToken);
        return open.Select(t => new TodoItem(
            "Tickets",
            t.Urgency == TicketUrgency.High ? $"Dringend: {t.Title}" : $"Neue Meldung: {t.Title}",
            $"/verwaltung/tickets/{t.Id}",
            t.Urgency == TicketUrgency.High ? Urgency.Urgent : Urgency.Soon,
            "Prüfen, einem Hausmeister oder Handwerker zuweisen oder den Mieter informieren.",
            null)).ToList();
    }
}

/// <summary>Tickets a person reported or works on: prevents deleting the person and provides the retention date.</summary>
internal sealed class TicketPersonReferences(MietHahnDbContext db) : MietHahn.Application.People.IPersonReference
{
    public async Task<IReadOnlyList<MietHahn.Application.People.PersonRelation>> ReferencesAsync(IReadOnlyCollection<Guid> personIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(personIds);
        var tickets = await db.Set<Ticket>().AsNoTracking()
            .Where(t => (t.ReporterId != null && personIds.Contains(t.ReporterId.Value)) || (t.AssigneeId != null && personIds.Contains(t.AssigneeId.Value)))
            .ToListAsync(cancellationToken);
        return tickets.SelectMany(t => new[] { (Person: t.ReporterId, Role: "Meldung"), (Person: t.AssigneeId, Role: "Auftrag") }
                .Where(x => x.Person is { } p && personIds.Contains(p))
                .Select(x => new MietHahn.Application.People.PersonRelation(
                    x.Person!.Value,
                    x.Role,
                    t.Title,
                    t.Status != TicketStatus.Closed,
                    t.ClosedAt is { } closed ? GermanTime.Today(closed) : null,
                    $"/verwaltung/tickets/{t.Id}")))
            .ToList();
    }
}
