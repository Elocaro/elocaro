using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MietHahn.Application.Accounts;
using MietHahn.Application.Common;
using MietHahn.Application.Communication;
using MietHahn.Application.Documents;
using MietHahn.Application.Tickets;
using MietHahn.Domain.Admin;
using MietHahn.Domain.Common;
using MietHahn.Domain.Leases;
using MietHahn.Domain.People;
using MietHahn.Domain.Properties;
using MietHahn.Domain.Receipts;
using MietHahn.Domain.Tickets;
using MietHahn.Infrastructure.Common;
using MietHahn.Infrastructure.Identity;
using MietHahn.Infrastructure.Leases;
using MietHahn.Infrastructure.Persistence;

namespace MietHahn.Infrastructure.Tickets;

internal sealed class TicketService(MietHahnDbContext db, IMailSender mail, IOptions<ApplicationOptions> app, TimeProvider clock) : ITickets
{
    private enum Viewer
    {
        None,
        Admin,
        Tenant,
        Service,
    }

    public async Task<IReadOnlyList<TicketRow>> ListAsync(ClaimsPrincipal user, bool openOnly = true, CancellationToken cancellationToken = default)
    {
        var (viewer, persons) = await ViewerAsync(user, cancellationToken);
        var tickets = await VisibleTickets(viewer, persons).AsNoTracking()
            .Where(t => !openOnly || t.Status != TicketStatus.Closed)
            .OrderBy(t => t.Status == TicketStatus.Closed).ThenByDescending(t => t.Urgency).ThenByDescending(t => t.ReportedAt)
            .ToListAsync(cancellationToken);
        return await RowsAsync(tickets, cancellationToken);
    }

    public async Task<TicketView?> GetAsync(ClaimsPrincipal user, Guid id, CancellationToken cancellationToken = default)
    {
        var (viewer, persons) = await ViewerAsync(user, cancellationToken);
        var ticket = await VisibleTickets(viewer, persons).AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (ticket is null)
        {
            return null;
        }

        var row = (await RowsAsync([ticket], cancellationToken))[0];
        var comments = await db.Set<TicketComment>().AsNoTracking()
            .Where(c => c.TicketId == id && (viewer == Viewer.Admin || !c.IsInternal)).OrderBy(c => c.CreatedAt).ToListAsync(cancellationToken);
        var photos = await db.Set<TicketPhoto>().AsNoTracking().Where(p => p.TicketId == id).OrderBy(p => p.CreatedAt).ToListAsync(cancellationToken);
        var commentIds = comments.Select(c => c.Id).ToHashSet();
        var views = comments.Select(c => new TicketCommentView(c.Id, c.AuthorName, c.Text, c.IsInternal, c.CreatedAt, photos.Where(p => p.CommentId == c.Id).Select(p => p.FileId).ToList())).ToList();
        var canComment = ticket.Status != TicketStatus.Closed;
        IReadOnlyList<TicketStatus> statuses = viewer switch
        {
            Viewer.Admin => Enum.GetValues<TicketStatus>().Where(s => s != ticket.Status).ToList(),
            Viewer.Service when ticket.Status != TicketStatus.Closed => new[] { TicketStatus.InProgress, TicketStatus.Done }.Where(s => s != ticket.Status).ToList(),
            _ => [],
        };

        return new TicketView(
            row,
            ticket.Description,
            ticket.Location,
            viewer == Viewer.Service ? null : ticket.AccessTimes,
            ticket.AssigneeId,
            viewer == Viewer.Admin ? ticket.ReceiptId : null,
            ticket.ClosedAt,
            photos.Where(p => p.CommentId is null).Select(p => p.FileId).ToList(),
            views,
            canComment,
            statuses);
    }

    public async Task<IReadOnlyList<ReportTarget>> ReportTargetsAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        var (viewer, persons) = await ViewerAsync(user, cancellationToken);
        if (viewer != Viewer.Tenant)
        {
            return [];
        }

        var today = GermanTime.Today(clock.GetUtcNow());
        var leases = await db.Set<Lease>().AsNoTracking()
            .Where(v => v.Parties.Any(p => persons.Contains(p.PersonId) && p.Role == PartyRole.Tenant) && (v.Term.To == null || v.Term.To >= today))
            .ToListAsync(cancellationToken);
        var units = await RentalUnit.GetAsync(db, cancellationToken);
        return leases.OrderBy(v => v.Start).Select(v =>
        {
            var unit = units.GetValueOrDefault(v.RentalUnitId);
            return new ReportTarget(v.Id, v.UnitId, unit?.PropertyId ?? Guid.Empty, unit?.Name ?? "", unit?.Property ?? "");
        }).Where(t => t.PropertyId != Guid.Empty).ToList();
    }

    public Task<Result> ReportAsync(ClaimsPrincipal user, TicketReport input, IReadOnlyList<Guid> fileIds, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(input);
            var (viewer, persons) = await ViewerAsync(user, cancellationToken);
            if (viewer != Viewer.Tenant)
            {
                throw new RuleViolationException("Nur Mieter können eine Meldung abgeben.");
            }

            var targets = await ReportTargetsAsync(user, cancellationToken);
            var target = (input.LeaseId is { } leaseId ? targets.FirstOrDefault(t => t.LeaseId == leaseId) : targets.Count > 0 ? targets[0] : null)
                ?? throw new RuleViolationException("Ihrem Konto ist kein laufendes Mietverhältnis zugeordnet.");
            var reporter = await db.Set<Person>().AsNoTracking().Where(p => persons.Contains(p.Id)).OrderBy(p => p.CreatedAt).Select(p => p.Id).FirstAsync(cancellationToken);
            var ticket = new Ticket(input.Title, input.Description, input.Category, input.Urgency, target.PropertyId, input.CommonArea ? null : target.UnitId, target.LeaseId, reporter, clock.GetUtcNow())
            {
                Location = Clean(input.Location),
                AccessTimes = Clean(input.AccessTimes),
            };
            await AddAsync(ticket, fileIds, cancellationToken);

            var admins = await AdminRecipients.GetAsync(db, cancellationToken);
            if (admins.Count > 0)
            {
                await mail.EnqueueAsync(new MailRequest(admins, "ticket-update", new
                {
                    Salutation = "Hallo,",
                    Title = ticket.Title,
                    Message = $"im Mieterportal wurde eine neue Meldung abgegeben ({(ticket.Urgency == TicketUrgency.High ? "dringend" : "normale Dringlichkeit")}).",
                    Link = app.Value.Link($"/verwaltung/tickets/{ticket.Id}").ToString(),
                    SenderName = "MietHahn",
                }, Reference: $"ticket:{ticket.Id}"), cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
            }

            return ticket.Id;
        });

    public Task<Result> CreateAsync(ClaimsPrincipal user, AdminTicketInput input, IReadOnlyList<Guid> fileIds, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(input);
            await RequireAdminAsync(user, cancellationToken);
            var propertyId = input.PropertyId ?? throw new RuleViolationException("Bitte das Objekt wählen.");
            if (!await db.Set<Property>().AnyAsync(o => o.Id == propertyId, cancellationToken)
                || (input.UnitId is { } unitId && !await db.Set<Unit>().AnyAsync(e => e.Id == unitId && e.PropertyId == propertyId, cancellationToken)))
            {
                throw new RuleViolationException("Objekt oder Einheit wurde nicht gefunden.");
            }

            var ticket = new Ticket(input.Title, input.Description, input.Category, input.Urgency, propertyId, input.UnitId, null, null, clock.GetUtcNow()) { Location = Clean(input.Location) };
            if (input.AssigneeId is { } assigneeId)
            {
                await EnsureAssigneeAsync(assigneeId, cancellationToken);
                ticket.Assign(assigneeId, input.AppointmentOn);
            }

            await AddAsync(ticket, fileIds, cancellationToken);
            if (ticket.AssigneeId is not null)
            {
                await NotifyAsync(ticket, "Ihnen wurde ein neuer Auftrag zugewiesen.", assigneeOnly: true, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
            }

            return ticket.Id;
        });

    public Task<Result> CommentAsync(ClaimsPrincipal user, Guid id, string text, bool isInternal, IReadOnlyList<Guid> fileIds, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var (viewer, persons) = await ViewerAsync(user, cancellationToken);
            var ticket = await VisibleTickets(viewer, persons).SingleOrDefaultAsync(t => t.Id == id, cancellationToken) ?? throw new NotFoundException();
            if (ticket.Status == TicketStatus.Closed)
            {
                throw new RuleViolationException("Das Ticket ist geschlossen.");
            }

            if (isInternal && viewer != Viewer.Admin)
            {
                throw new RuleViolationException("Interne Kommentare sind nur für die Verwaltung möglich.");
            }

            _ = Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId);
            var name = await db.Set<ApplicationUser>().Where(u => u.Id == userId).Select(u => u.DisplayName).FirstOrDefaultAsync(cancellationToken) ?? "Unbekannt";
            var comment = new TicketComment(id, text, isInternal, userId, name);
            db.Add(comment);
            foreach (var fileId in fileIds.Take(Ticket.MaxPhotos))
            {
                db.Add(new TicketPhoto(id, fileId, comment.Id));
            }

            if (!isInternal)
            {
                await NotifyAsync(ticket, $"{name} hat einen Kommentar hinzugefügt: „{comment.Text}“", viewer, cancellationToken);
            }

            await db.SaveChangesAsync(cancellationToken);
            return comment.Id;
        });

    public Task<Result> SetStatusAsync(ClaimsPrincipal user, Guid id, TicketStatus status, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var (viewer, persons) = await ViewerAsync(user, cancellationToken);
            var ticket = await VisibleTickets(viewer, persons).SingleOrDefaultAsync(t => t.Id == id, cancellationToken) ?? throw new NotFoundException();
            var allowed = viewer == Viewer.Admin || (viewer == Viewer.Service && status is TicketStatus.InProgress or TicketStatus.Done && ticket.Status != TicketStatus.Closed);
            if (!allowed)
            {
                throw new RuleViolationException("Diesen Status dürfen Sie nicht setzen.");
            }

            if (ticket.Status != status)
            {
                ticket.SetStatus(status, clock.GetUtcNow());
                await NotifyAsync(ticket, $"der Stand hat sich geändert: {TicketTexts.Status(status)}.", viewer, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
            }

            return id;
        });

    public Task<Result> AssignAsync(ClaimsPrincipal user, Guid id, Guid? assigneeId, DateOnly? appointmentOn, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            await RequireAdminAsync(user, cancellationToken);
            var ticket = await db.Set<Ticket>().SingleOrDefaultAsync(t => t.Id == id, cancellationToken) ?? throw new NotFoundException();
            if (assigneeId is { } person)
            {
                await EnsureAssigneeAsync(person, cancellationToken);
            }

            var changed = ticket.AssigneeId != assigneeId;
            ticket.Assign(assigneeId, appointmentOn);
            if (changed && assigneeId is not null)
            {
                if (ticket.Status is TicketStatus.New or TicketStatus.InReview)
                {
                    ticket.SetStatus(TicketStatus.Commissioned, clock.GetUtcNow());
                }

                await NotifyAsync(ticket, "Ihnen wurde ein neuer Auftrag zugewiesen.", assigneeOnly: true, cancellationToken);
            }

            await db.SaveChangesAsync(cancellationToken);
            return id;
        });

    public Task<Result> LinkReceiptAsync(ClaimsPrincipal user, Guid id, Guid? receiptId, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            await RequireAdminAsync(user, cancellationToken);
            var ticket = await db.Set<Ticket>().SingleOrDefaultAsync(t => t.Id == id, cancellationToken) ?? throw new NotFoundException();
            if (receiptId is { } receipt && !await db.Set<Receipt>().AnyAsync(b => b.Id == receipt, cancellationToken))
            {
                throw new RuleViolationException("Der Beleg wurde nicht gefunden.");
            }

            ticket.ReceiptId = receiptId;
            await db.SaveChangesAsync(cancellationToken);
            return id;
        });

    public async Task<IReadOnlyList<ReferenceOption>> AssigneesAsync(CancellationToken cancellationToken = default) =>
        (await db.Set<Person>().AsNoTracking()
            .Where(p => p.UserId != null && p.AnonymizedAt == null && (p.ContactType == ContactType.Caretaker || p.ContactType == ContactType.Tradesperson))
            .OrderBy(p => p.LastName).ToListAsync(cancellationToken))
        .Select(p => new ReferenceOption(p.Id, p.DisplayName)).ToList();

    public async Task<IReadOnlyList<TicketCost>> CostsAsync(CancellationToken cancellationToken = default)
    {
        var tickets = await db.Set<Ticket>().AsNoTracking().Where(t => t.ReceiptId != null).ToListAsync(cancellationToken);
        var receiptIds = tickets.Select(t => t.ReceiptId!.Value).ToList();
        var amounts = await db.Set<Receipt>().AsNoTracking().Where(b => receiptIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.Gross, cancellationToken);
        return (await RowsAsync(tickets, cancellationToken)).Select(r => new TicketCost(r.Id, r.Title, r.Property, r.Unit, amounts.GetValueOrDefault(tickets.First(t => t.Id == r.Id).ReceiptId!.Value))).ToList();
    }

    private async Task AddAsync(Ticket ticket, IReadOnlyList<Guid> fileIds, CancellationToken cancellationToken)
    {
        if (fileIds.Count > Ticket.MaxPhotos)
        {
            throw new RuleViolationException($"Höchstens {Ticket.MaxPhotos} Fotos je Meldung.");
        }

        db.Add(ticket);
        foreach (var fileId in fileIds)
        {
            db.Add(new TicketPhoto(ticket.Id, fileId, null));
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<(Viewer Viewer, List<Guid> Persons)> ViewerAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (user.IsInRole(Roles.Admin) || user.HasClaim(Permissions.ClaimType, nameof(AccessRight.MasterData)))
        {
            return (Viewer.Admin, []);
        }

        var viewer = user.IsInRole(Roles.Tenant) ? Viewer.Tenant : user.IsInRole(Roles.Caretaker) || user.IsInRole(Roles.Tradesperson) ? Viewer.Service : Viewer.None;
        if (viewer == Viewer.None || !Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return (Viewer.None, []);
        }

        var persons = await db.Set<Person>().AsNoTracking().Where(p => p.UserId == userId && p.AnonymizedAt == null).Select(p => p.Id).ToListAsync(cancellationToken);
        return (persons.Count == 0 ? Viewer.None : viewer, persons);
    }

    private async Task RequireAdminAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if ((await ViewerAsync(user, cancellationToken)).Viewer != Viewer.Admin)
        {
            throw new RuleViolationException("Dafür fehlt die Berechtigung.");
        }
    }

    private IQueryable<Ticket> VisibleTickets(Viewer viewer, List<Guid> persons) => viewer switch
    {
        Viewer.Admin => db.Set<Ticket>(),
        Viewer.Tenant => db.Set<Ticket>().Where(t => t.ReporterId != null && persons.Contains(t.ReporterId.Value)),
        Viewer.Service => db.Set<Ticket>().Where(t => t.AssigneeId != null && persons.Contains(t.AssigneeId.Value)),
        _ => db.Set<Ticket>().Where(_ => false),
    };

    private async Task EnsureAssigneeAsync(Guid personId, CancellationToken cancellationToken)
    {
        if (!await db.Set<Person>().AnyAsync(p => p.Id == personId && p.UserId != null && p.AnonymizedAt == null && (p.ContactType == ContactType.Caretaker || p.ContactType == ContactType.Tradesperson), cancellationToken))
        {
            throw new RuleViolationException("Zugewiesen werden können nur Hausmeister und Handwerker mit Zugang zum Servicebereich.");
        }
    }

    /// <summary>Tells the tenant and the assignee (not the one who just acted) about a change.</summary>
    private Task NotifyAsync(Ticket ticket, string message, Viewer actor, CancellationToken cancellationToken) => NotifyAsync(ticket, message, false, actor, cancellationToken);

    private Task NotifyAsync(Ticket ticket, string message, bool assigneeOnly, CancellationToken cancellationToken) => NotifyAsync(ticket, message, assigneeOnly, Viewer.Admin, cancellationToken);

    private async Task NotifyAsync(Ticket ticket, string message, bool assigneeOnly, Viewer actor, CancellationToken cancellationToken)
    {
        var sender = (await Admin.LandlordProfiles.ForPropertyAsync(db, ticket.PropertyId, cancellationToken))?.Name ?? "Ihre Hausverwaltung";
        var recipients = new List<(Guid Person, string Link)>();
        if (!assigneeOnly && actor != Viewer.Tenant && ticket.ReporterId is { } reporter)
        {
            recipients.Add((reporter, $"/portal/tickets/{ticket.Id}"));
        }

        if (ticket.AssigneeId is { } assignee && actor != Viewer.Service)
        {
            recipients.Add((assignee, $"/service/tickets/{ticket.Id}"));
        }

        var ids = recipients.Select(r => r.Person).ToList();
        var people = await db.Set<Person>().AsNoTracking().Where(p => ids.Contains(p.Id) && p.Email != null && p.AnonymizedAt == null).ToDictionaryAsync(p => p.Id, cancellationToken);
        foreach (var (personId, link) in recipients)
        {
            if (!people.TryGetValue(personId, out var person))
            {
                continue;
            }

            await mail.EnqueueAsync(new MailRequest([person.Email!], "ticket-update", new
            {
                Salutation = LetterParts.SalutationOf([person]),
                Title = ticket.Title,
                Message = message,
                Link = app.Value.Link(link).ToString(),
                SenderName = sender,
            }, Reference: $"ticket:{ticket.Id}"), cancellationToken);
        }
    }

    private async Task<List<TicketRow>> RowsAsync(List<Ticket> tickets, CancellationToken cancellationToken)
    {
        if (tickets.Count == 0)
        {
            return [];
        }

        var properties = await db.Set<Property>().AsNoTracking().ToDictionaryAsync(o => o.Id, o => o.Name, cancellationToken);
        var units = await db.Set<Unit>().AsNoTracking().ToDictionaryAsync(e => e.Id, e => e.Name, cancellationToken);
        var personIds = tickets.SelectMany(t => new[] { t.ReporterId, t.AssigneeId }).Where(i => i is not null).Select(i => i!.Value).Distinct().ToList();
        var people = await db.Set<Person>().AsNoTracking().Where(p => personIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.DisplayName, cancellationToken);
        return tickets.Select(t => new TicketRow(
            t.Id,
            t.Title,
            t.Category,
            t.Urgency,
            t.Status,
            properties.GetValueOrDefault(t.PropertyId, ""),
            t.UnitId is { } u ? units.GetValueOrDefault(u) : null,
            t.ReporterId is { } r ? people.GetValueOrDefault(r) : null,
            t.AssigneeId is { } a ? people.GetValueOrDefault(a) : null,
            t.ReportedAt,
            t.AppointmentOn)).ToList();
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal static class TicketTexts
{
    public static string Status(TicketStatus status) => status switch
    {
        TicketStatus.New => "neu",
        TicketStatus.InReview => "in Prüfung",
        TicketStatus.Commissioned => "beauftragt",
        TicketStatus.InProgress => "in Arbeit",
        TicketStatus.Done => "erledigt",
        _ => "geschlossen",
    };
}
