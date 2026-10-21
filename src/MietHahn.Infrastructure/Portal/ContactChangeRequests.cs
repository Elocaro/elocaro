using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MietHahn.Application.Accounts;
using MietHahn.Application.Common;
using MietHahn.Application.People;
using MietHahn.Application.Todos;
using MietHahn.Domain.Common;
using MietHahn.Domain.People;
using MietHahn.Infrastructure.Common;
using MietHahn.Infrastructure.Persistence;

namespace MietHahn.Infrastructure.Portal;

internal sealed class ContactChangeRequests(MietHahnDbContext db, TimeProvider clock) : IContactChangeRequests
{
    public async Task<IReadOnlyList<ContactChangeView>> OpenAsync(CancellationToken cancellationToken = default)
    {
        var requests = await db.Set<ContactChangeRequest>().AsNoTracking().Where(a => a.Status == ContactChangeStatus.Open).OrderBy(a => a.RequestedAt).ToListAsync(cancellationToken);
        var ids = requests.Select(a => a.PersonId).ToList();
        var people = await db.Set<Person>().AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, cancellationToken);
        return requests.Where(a => people.ContainsKey(a.PersonId)).Select(a =>
        {
            var p = people[a.PersonId];
            return new ContactChangeView(a.Id, p.Id, p.DisplayName, a.RequestedAt, p.Email, p.Phone, p.Mobile, a.Email, a.Phone, a.Mobile);
        }).ToList();
    }

    public Task<Result> ApproveAsync(Guid id, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var request = await db.Set<ContactChangeRequest>().SingleOrDefaultAsync(a => a.Id == id, cancellationToken) ?? throw new NotFoundException();
            var person = await db.Set<Person>().SingleAsync(p => p.Id == request.PersonId, cancellationToken);
            request.Approve(person, clock.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
            return id;
        });

    public Task<Result> RejectAsync(Guid id, string? note, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var request = await db.Set<ContactChangeRequest>().SingleOrDefaultAsync(a => a.Id == id, cancellationToken) ?? throw new NotFoundException();
            request.Reject(note, clock.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
            return id;
        });
}

/// <summary>Dashboard: contact data changes requested by tenants that wait for confirmation.</summary>
internal sealed class ContactChangeTodos(MietHahnDbContext db) : ITodoSource
{
    public async Task<IReadOnlyList<TodoItem>> TodosAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!user.IsInRole(Roles.Admin) && !user.HasClaim(Permissions.ClaimType, nameof(AccessRight.MasterData)))
        {
            return [];
        }

        var open = await db.Set<ContactChangeRequest>().AsNoTracking().Where(a => a.Status == ContactChangeStatus.Open).OrderBy(a => a.RequestedAt).ToListAsync(cancellationToken);
        var names = await db.Set<Person>().AsNoTracking().Where(p => open.Select(a => a.PersonId).Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.DisplayName, cancellationToken);
        return open.Select(a => new TodoItem(
            "Personen",
            $"Änderung der Kontaktdaten bestätigen: {names.GetValueOrDefault(a.PersonId, "Mieter")}",
            "/verwaltung/personen/aenderungen",
            Urgency.Info,
            "Der Mieter hat im Portal neue Kontaktdaten angegeben; sie gelten erst nach Ihrer Bestätigung.",
            null)).ToList();
    }
}
