using System.Security.Claims;
using Elocaro.Application.Common;
using Elocaro.Application.Communication;
using Elocaro.Application.Tickets;
using Elocaro.Domain.Common;
using Elocaro.Domain.People;
using Elocaro.Domain.Properties;
using Elocaro.Domain.Tickets;
using Elocaro.Infrastructure.Common;
using Elocaro.Infrastructure.Identity;
using Elocaro.Infrastructure.Notices;
using Elocaro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Elocaro.Infrastructure.Tickets;

internal sealed class ContractorLinks(ElocaroDbContext db, IMailSender mail, IOptions<ApplicationOptions> app, TimeProvider clock) : IContractorLinks
{
    public async Task<LinkCreation> CreateAsync(ClaimsPrincipal user, Guid ticketId, string recipient, int days, CancellationToken cancellationToken = default)
    {
        try
        {
            RequireAdmin(user);
            if (string.IsNullOrWhiteSpace(recipient))
            {
                throw new RuleViolationException("Bitte angeben, für wen der Link ist (Firma oder Name).");
            }

            var ticket = await db.Set<Ticket>().AsNoTracking().SingleOrDefaultAsync(t => t.Id == ticketId, cancellationToken) ?? throw new NotFoundException();
            if (ticket.Status == TicketStatus.Closed)
            {
                throw new RuleViolationException("Das Ticket ist geschlossen.");
            }

            var token = TicketAccessLink.NewToken();
            var expires = clock.GetUtcNow().AddDays(Math.Clamp(days, 1, TicketAccessLink.MaxDays));
            var link = new TicketAccessLink(ticketId, TicketAccessLink.Hash(token), recipient, expires);
            db.Add(link);
            await db.SaveChangesAsync(cancellationToken);
            return new LinkCreation(Result.Ok(link.Id), app.Value.Link($"/auftrag/{token}").ToString(), expires);
        }
        catch (Exception ex) when (ex is RuleViolationException or NotFoundException)
        {
            return new LinkCreation(Result.Failed(ex is NotFoundException ? "Das Ticket wurde nicht gefunden." : ex.Message));
        }
    }

    public async Task<IReadOnlyList<LinkRow>> ListAsync(ClaimsPrincipal user, Guid ticketId, CancellationToken cancellationToken = default)
    {
        RequireAdmin(user);
        var now = clock.GetUtcNow();
        return (await db.Set<TicketAccessLink>().AsNoTracking().Where(l => l.TicketId == ticketId).OrderByDescending(l => l.CreatedAt).ToListAsync(cancellationToken))
            .Select(l => new LinkRow(l.Id, l.Recipient, l.ExpiresAt, l.IsValid(now), l.RevokedAt)).ToList();
    }

    public Task<Result> RevokeAsync(ClaimsPrincipal user, Guid linkId, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            RequireAdmin(user);
            var link = await db.Set<TicketAccessLink>().SingleOrDefaultAsync(l => l.Id == linkId, cancellationToken) ?? throw new NotFoundException();
            link.Revoke(clock.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
            return linkId;
        });

    public async Task<bool> IsValidAsync(string token, CancellationToken cancellationToken = default) => await FindAsync(token, false, cancellationToken) is not null;

    public async Task<ContractorTicket?> GetAsync(string token, CancellationToken cancellationToken = default)
    {
        var found = await FindAsync(token, false, cancellationToken);
        if (found is not { } f)
        {
            return null;
        }

        var (link, ticket) = f;
        var property = await db.Set<Property>().AsNoTracking().Where(o => o.Id == ticket.PropertyId).Select(o => o.Name).FirstOrDefaultAsync(cancellationToken) ?? "";
        var unit = ticket.UnitId is { } u ? await db.Set<Unit>().AsNoTracking().Where(e => e.Id == u).Select(e => e.Name).FirstOrDefaultAsync(cancellationToken) : null;
        var comments = await db.Set<TicketComment>().AsNoTracking().Where(c => c.TicketId == ticket.Id && !c.IsInternal).OrderBy(c => c.CreatedAt).ToListAsync(cancellationToken);
        var photos = await db.Set<TicketPhoto>().AsNoTracking().Where(p => p.TicketId == ticket.Id).OrderBy(p => p.CreatedAt).ToListAsync(cancellationToken);
        var commentViews = comments.Select(c => new TicketCommentView(c.Id, c.AuthorName, c.Text, false, c.CreatedAt, photos.Where(p => p.CommentId == c.Id).Select(p => p.FileId).ToList())).ToList();
        return new ContractorTicket(
            ticket.Title,
            ticket.Description,
            ticket.Category,
            ticket.Urgency,
            ticket.Status,
            property,
            unit,
            ticket.Location,
            ticket.AppointmentOn,
            link.Recipient,
            link.ExpiresAt,
            photos.Where(p => p.CommentId is null).Select(p => p.FileId).ToList(),
            commentViews,
            await TicketWorkService.ViewAsync(db, ticket.Id, true, cancellationToken));
    }

    public Task<Result> CommentAsync(string token, string text, IReadOnlyList<Guid> fileIds, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var (link, ticket) = await RequireAsync(token, cancellationToken);
            if (fileIds.Count > Ticket.MaxPhotos)
            {
                throw new RuleViolationException($"Höchstens {Ticket.MaxPhotos} Fotos je Kommentar.");
            }

            var comment = new TicketComment(ticket.Id, text, false, null, link.Recipient);
            db.Add(comment);
            foreach (var fileId in fileIds)
            {
                db.Add(new TicketPhoto(ticket.Id, fileId, comment.Id));
            }

            await db.SaveChangesAsync(cancellationToken);
            await NotifyAsync(ticket, link, $"{link.Recipient} hat einen Kommentar hinzugefügt: „{comment.Text}“", cancellationToken);
            return comment.Id;
        });

    public Task<Result> SetStatusAsync(string token, TicketStatus status, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var (link, ticket) = await RequireAsync(token, cancellationToken);
            if (status is not (TicketStatus.InProgress or TicketStatus.Done))
            {
                throw new RuleViolationException("Diesen Status dürfen Sie nicht setzen.");
            }

            if (ticket.Status != status)
            {
                ticket.SetStatus(status, clock.GetUtcNow());
                await db.SaveChangesAsync(cancellationToken);
                await NotifyAsync(ticket, link, $"der Stand hat sich geändert: {TicketTexts.Status(status)}.", cancellationToken);
            }

            return ticket.Id;
        });

    public Task<Result> AddWorkAsync(string token, WorkInput input, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(input);
            var (link, ticket) = await RequireAsync(token, cancellationToken);
            var entry = TicketWorkService.AddWork(db, clock, ticket.Id, input, null, link.Recipient);
            await db.SaveChangesAsync(cancellationToken);
            return entry.Id;
        });

    public Task<Result> AddInvoiceAsync(string token, Guid fileId, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var (link, ticket) = await RequireAsync(token, cancellationToken);
            var invoice = new TicketInvoice(ticket.Id, fileId, link.Recipient);
            db.Add(invoice);
            await db.SaveChangesAsync(cancellationToken);
            await NotifyAsync(ticket, link, "eine Rechnung wurde hochgeladen.", cancellationToken, adminsOnly: true);
            return invoice.Id;
        });

    public async Task<Guid?> FileOfAsync(string token, Guid fileId, CancellationToken cancellationToken = default)
    {
        if (await FindAsync(token, false, cancellationToken) is not { } found)
        {
            return null;
        }

        var ticketId = found.Ticket.Id;
        var internalComments = db.Set<TicketComment>().Where(c => c.TicketId == ticketId && c.IsInternal).Select(c => c.Id);
        var photo = await db.Set<TicketPhoto>().AnyAsync(p => p.TicketId == ticketId && p.FileId == fileId && (p.CommentId == null || !internalComments.Contains(p.CommentId.Value)), cancellationToken);
        var invoice = await db.Set<TicketInvoice>().AnyAsync(i => i.TicketId == ticketId && i.FileId == fileId, cancellationToken);
        return photo || invoice ? fileId : null;
    }

    private async Task<(TicketAccessLink Link, Ticket Ticket)> RequireAsync(string token, CancellationToken cancellationToken)
    {
        var found = await FindAsync(token, true, cancellationToken);
        return found is { } f ? f : throw new RuleViolationException("Dieser Link ist abgelaufen oder nicht mehr gültig.");
    }

    /// <summary>The link and ticket behind a token: unknown, expired, revoked and closed tickets all look the same.</summary>
    private async Task<(TicketAccessLink Link, Ticket Ticket)?> FindAsync(string token, bool tracked, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 100)
        {
            return null;
        }

        var hash = TicketAccessLink.Hash(token);
        var links = tracked ? db.Set<TicketAccessLink>() : db.Set<TicketAccessLink>().AsNoTracking();
        var link = await links.SingleOrDefaultAsync(l => l.TokenHash == hash, cancellationToken);
        if (link is null || !link.IsValid(clock.GetUtcNow()))
        {
            return null;
        }

        var tickets = tracked ? db.Set<Ticket>() : db.Set<Ticket>().AsNoTracking();
        var ticket = await tickets.SingleOrDefaultAsync(t => t.Id == link.TicketId, cancellationToken);
        return ticket is null || ticket.Status == TicketStatus.Closed ? null : (link, ticket);
    }

    private static void RequireAdmin(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!TenantScope.IsAdmin(user))
        {
            throw new RuleViolationException("Dafür fehlt die Berechtigung.");
        }
    }

    /// <summary>Tells the administration and (unless <paramref name="adminsOnly"/>) the reporting tenant what the tradesperson did.</summary>
    private async Task NotifyAsync(Ticket ticket, TicketAccessLink link, string message, CancellationToken cancellationToken, bool adminsOnly = false)
    {
        var admins = await AdminRecipients.GetAsync(db, cancellationToken);
        if (admins.Count > 0)
        {
            await mail.EnqueueAsync(new MailRequest(admins, "ticket-update", new
            {
                Salutation = "Hallo,",
                Title = ticket.Title,
                Message = $"{link.Recipient}: {message}",
                Link = app.Value.Link($"/verwaltung/tickets/{ticket.Id}").ToString(),
                SenderName = "Elocaro",
            }, Reference: $"ticket:{ticket.Id}"), cancellationToken);
        }

        if (!adminsOnly && ticket.ReporterId is { } reporter
            && await db.Set<Person>().AsNoTracking().SingleOrDefaultAsync(p => p.Id == reporter && p.Email != null && p.AnonymizedAt == null, cancellationToken) is { } person)
        {
            await mail.EnqueueAsync(new MailRequest([person.Email!], "ticket-update", new
            {
                Salutation = LetterParts.SalutationOf([person]),
                Title = ticket.Title,
                Message = message,
                Link = app.Value.Link($"/portal/tickets/{ticket.Id}").ToString(),
                SenderName = "Ihre Hausverwaltung",
            }, Reference: $"ticket:{ticket.Id}"), cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
