using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Elocaro.Application.Common;
using Elocaro.Domain.Tickets;

namespace Elocaro.Application.Tickets;

/// <summary>Working time, material and invoices of a ticket for the administration and the assignee with an account.</summary>
public interface ITicketWork
{
    Task<TicketWorkView?> GetAsync(ClaimsPrincipal user, Guid ticketId, CancellationToken cancellationToken = default);

    Task<Result> AddWorkAsync(ClaimsPrincipal user, Guid ticketId, WorkInput input, CancellationToken cancellationToken = default);

    Task<Result> AddInvoiceAsync(ClaimsPrincipal user, Guid ticketId, Guid fileId, CancellationToken cancellationToken = default);

    /// <summary>Administration: the tickets whose invoice is this receipt, with the work reported on them (to check the invoice against the hours and material).</summary>
    Task<IReadOnlyList<ReceiptTicket>> ForReceiptAsync(ClaimsPrincipal user, Guid receiptId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Links for tradespeople without an account: one ticket, limited in time, closed with the ticket.
/// The administration creates and revokes links; the holder works with the secret token only.
/// </summary>
public interface IContractorLinks
{
    /// <summary>Administration: creates a link. The secret is returned once and not stored.</summary>
    Task<LinkCreation> CreateAsync(ClaimsPrincipal user, Guid ticketId, string recipient, int days, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LinkRow>> ListAsync(ClaimsPrincipal user, Guid ticketId, CancellationToken cancellationToken = default);

    Task<Result> RevokeAsync(ClaimsPrincipal user, Guid linkId, CancellationToken cancellationToken = default);

    /// <summary>The ticket behind a valid token, otherwise <c>null</c> (unknown, expired, revoked or closed).</summary>
    Task<ContractorTicket?> GetAsync(string token, CancellationToken cancellationToken = default);

    Task<Result> CommentAsync(string token, string text, IReadOnlyList<Guid> fileIds, CancellationToken cancellationToken = default);

    /// <summary>Only "in progress" and "done".</summary>
    Task<Result> SetStatusAsync(string token, TicketStatus status, CancellationToken cancellationToken = default);

    Task<Result> AddWorkAsync(string token, WorkInput input, CancellationToken cancellationToken = default);

    Task<Result> AddInvoiceAsync(string token, Guid fileId, CancellationToken cancellationToken = default);

    /// <summary>Checks the token (before a file is accepted).</summary>
    Task<bool> IsValidAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>A photo or invoice of this ticket; internal files are never delivered.</summary>
    Task<Guid?> FileOfAsync(string token, Guid fileId, CancellationToken cancellationToken = default);
}

public sealed class WorkInput
{
    public DateOnly? WorkedOn { get; set; }

    [Range(0, 1440, ErrorMessage = "Die Arbeitszeit muss zwischen 0 und 1440 Minuten liegen.")]
    public int Minutes { get; set; }

    [StringLength(500)]
    public string? Material { get; set; }

    [StringLength(1000)]
    public string? Note { get; set; }
}

public sealed record WorkRow(Guid Id, DateOnly WorkedOn, int Minutes, string? Material, string? Note, string Author);

public sealed record ReceiptTicket(Guid Id, string Title, TicketStatus Status, int Minutes, IReadOnlyList<WorkRow> Work);

public sealed record InvoiceRow(Guid Id, Guid FileId, string UploadedBy, DateTimeOffset At);

public sealed record TicketWorkView(IReadOnlyList<WorkRow> Work, IReadOnlyList<InvoiceRow> Invoices, bool CanAdd);

public sealed record LinkCreation(Result Result, string? Url = null, DateTimeOffset? ExpiresAt = null);

public sealed record LinkRow(Guid Id, string Recipient, DateTimeOffset ExpiresAt, bool Active, DateTimeOffset? RevokedAt);

/// <summary>What the holder of a link sees: no tenant contact data, no internal comments.</summary>
public sealed record ContractorTicket(
    string Title,
    string Description,
    TicketCategory Category,
    TicketUrgency Urgency,
    TicketStatus Status,
    string Property,
    string? Unit,
    string? Location,
    DateOnly? AppointmentOn,
    string Recipient,
    DateTimeOffset ExpiresAt,
    IReadOnlyList<Guid> FileIds,
    IReadOnlyList<TicketCommentView> Comments,
    TicketWorkView Work);
