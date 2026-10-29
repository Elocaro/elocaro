using System.Security.Claims;
using Elocaro.Application.Accounts;
using Elocaro.Application.Common;
using Elocaro.Application.Tickets;
using Elocaro.Domain.Common;
using Elocaro.Domain.People;
using Elocaro.Domain.Tickets;
using Elocaro.Infrastructure.Common;
using Elocaro.Infrastructure.Identity;
using Elocaro.Infrastructure.Notices;
using Elocaro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Elocaro.Infrastructure.Tickets;

internal sealed class TicketWorkService(ElocaroDbContext db, TimeProvider clock) : ITicketWork
{
    public async Task<TicketWorkView?> GetAsync(ClaimsPrincipal user, Guid ticketId, CancellationToken cancellationToken = default)
    {
        var (allowed, canAdd) = await AccessAsync(user, ticketId, cancellationToken);
        return allowed ? await ViewAsync(db, ticketId, canAdd, cancellationToken) : null;
    }

    public Task<Result> AddWorkAsync(ClaimsPrincipal user, Guid ticketId, WorkInput input, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(input);
            await RequireWriteAsync(user, ticketId, cancellationToken);
            var entry = AddWork(db, clock, ticketId, input, TenantScope.UserId(user), await NameAsync(user, cancellationToken));
            await db.SaveChangesAsync(cancellationToken);
            return entry.Id;
        });

    public async Task<IReadOnlyList<ReceiptTicket>> ForReceiptAsync(ClaimsPrincipal user, Guid receiptId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!TenantScope.IsAdmin(user))
        {
            return [];
        }

        var tickets = await db.Set<Ticket>().AsNoTracking().Where(t => t.ReceiptId == receiptId).OrderBy(t => t.ReportedAt).ToListAsync(cancellationToken);
        var result = new List<ReceiptTicket>();
        foreach (var ticket in tickets)
        {
            var work = (await ViewAsync(db, ticket.Id, false, cancellationToken)).Work;
            result.Add(new ReceiptTicket(ticket.Id, ticket.Title, ticket.Status, work.Sum(w => w.Minutes), work));
        }

        return result;
    }

    public Task<Result> AddInvoiceAsync(ClaimsPrincipal user, Guid ticketId, Guid fileId, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            await RequireWriteAsync(user, ticketId, cancellationToken);
            var invoice = new TicketInvoice(ticketId, fileId, await NameAsync(user, cancellationToken));
            db.Add(invoice);
            await db.SaveChangesAsync(cancellationToken);
            return invoice.Id;
        });

    internal static TicketWork AddWork(ElocaroDbContext db, TimeProvider clock, Guid ticketId, WorkInput input, Guid? userId, string author)
    {
        var today = GermanTime.Today(clock.GetUtcNow());
        var day = input.WorkedOn ?? today;
        if (day > today)
        {
            throw new RuleViolationException("Das Datum der Arbeit darf nicht in der Zukunft liegen.");
        }

        var entry = new TicketWork(ticketId, day, input.Minutes, input.Material, input.Note, userId, author);
        db.Add(entry);
        return entry;
    }

    internal static async Task<TicketWorkView> ViewAsync(ElocaroDbContext db, Guid ticketId, bool canAdd, CancellationToken cancellationToken)
    {
        var work = await db.Set<TicketWork>().AsNoTracking().Where(w => w.TicketId == ticketId).OrderBy(w => w.WorkedOn).ThenBy(w => w.CreatedAt).ToListAsync(cancellationToken);
        var invoices = await db.Set<TicketInvoice>().AsNoTracking().Where(i => i.TicketId == ticketId).OrderBy(i => i.CreatedAt).ToListAsync(cancellationToken);
        return new TicketWorkView(
            work.Select(w => new WorkRow(w.Id, w.WorkedOn, w.Minutes, w.Material, w.Note, w.AuthorName)).ToList(),
            invoices.Select(i => new InvoiceRow(i.Id, i.FileId, i.UploadedBy, i.CreatedAt)).ToList(),
            canAdd);
    }

    /// <summary>Administration reads everything; an assignee with an account reads and writes their own tickets as long as they are open.</summary>
    private async Task<(bool Allowed, bool CanAdd)> AccessAsync(ClaimsPrincipal user, Guid ticketId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        var ticket = await db.Set<Ticket>().AsNoTracking().Where(t => t.Id == ticketId).Select(t => new { t.AssigneeId, t.Status }).SingleOrDefaultAsync(cancellationToken);
        if (ticket is null)
        {
            return (false, false);
        }

        if (TenantScope.IsAdmin(user))
        {
            return (true, false);
        }

        if ((user.IsInRole(Roles.Caretaker) || user.IsInRole(Roles.Tradesperson)) && TenantScope.UserId(user) is { } userId && ticket.AssigneeId is { } assignee
            && await db.Set<Person>().AnyAsync(p => p.Id == assignee && p.UserId == userId, cancellationToken))
        {
            return (true, ticket.Status != TicketStatus.Closed);
        }

        return (false, false);
    }

    private async Task RequireWriteAsync(ClaimsPrincipal user, Guid ticketId, CancellationToken cancellationToken)
    {
        var (allowed, canAdd) = await AccessAsync(user, ticketId, cancellationToken);
        if (!allowed)
        {
            throw new NotFoundException();
        }

        if (!canAdd)
        {
            throw new RuleViolationException("Dafür fehlt die Berechtigung oder das Ticket ist geschlossen.");
        }
    }

    private async Task<string> NameAsync(ClaimsPrincipal user, CancellationToken cancellationToken) =>
        TenantScope.UserId(user) is { } id
            ? await db.Set<ApplicationUser>().Where(u => u.Id == id).Select(u => u.DisplayName).FirstOrDefaultAsync(cancellationToken) ?? "Unbekannt"
            : "Unbekannt";
}
