using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Elocaro.Application.Common;
using Elocaro.Application.Documents;
using Elocaro.Domain.Tickets;

namespace Elocaro.Application.Tickets;

/// <summary>
/// Damage reports and tasks. What a caller sees and may do depends on the account: administration (all tickets, internal
/// comments), tenant (own tickets, public comments) or caretaker/tradesperson (assigned tickets, public comments).
/// A ticket the account may not see is simply not found.
/// </summary>
public interface ITickets
{
    /// <summary>Administration: all tickets (<paramref name="openOnly"/>: without closed ones). Tenants: their own. Service: assigned ones.</summary>
    Task<IReadOnlyList<TicketRow>> ListAsync(ClaimsPrincipal user, bool openOnly = true, CancellationToken cancellationToken = default);

    Task<TicketView?> GetAsync(ClaimsPrincipal user, Guid id, CancellationToken cancellationToken = default);

    /// <summary>Tenant: report a problem in an own rental unit or in the common areas.</summary>
    Task<Result> ReportAsync(ClaimsPrincipal user, TicketReport input, IReadOnlyList<Guid> fileIds, CancellationToken cancellationToken = default);

    /// <summary>The leases (units) a tenant can report for; empty if the account is not linked to a tenant.</summary>
    Task<IReadOnlyList<ReportTarget>> ReportTargetsAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default);

    /// <summary>Administration: create a ticket (e.g. from a maintenance task).</summary>
    Task<Result> CreateAsync(ClaimsPrincipal user, AdminTicketInput input, IReadOnlyList<Guid> fileIds, CancellationToken cancellationToken = default);

    /// <summary>Comment (and photos). Only the administration may write internal comments.</summary>
    Task<Result> CommentAsync(ClaimsPrincipal user, Guid id, string text, bool isInternal, IReadOnlyList<Guid> fileIds, CancellationToken cancellationToken = default);

    /// <summary>Administration: any status. Assignee: only "in progress" and "done". Notifies the people involved.</summary>
    Task<Result> SetStatusAsync(ClaimsPrincipal user, Guid id, TicketStatus status, CancellationToken cancellationToken = default);

    /// <summary>Administration: assign to a caretaker or tradesperson (person with an account) and set the appointment.</summary>
    Task<Result> AssignAsync(ClaimsPrincipal user, Guid id, Guid? assigneeId, DateOnly? appointmentOn, CancellationToken cancellationToken = default);

    /// <summary>Administration: link the invoice (receipt) of the work.</summary>
    Task<Result> LinkReceiptAsync(ClaimsPrincipal user, Guid id, Guid? receiptId, CancellationToken cancellationToken = default);

    /// <summary>Persons that can be assigned: contacts of type caretaker or tradesperson with a user account.</summary>
    Task<IReadOnlyList<ReferenceOption>> AssigneesAsync(CancellationToken cancellationToken = default);

    /// <summary>Costs of the tickets with a linked invoice, per unit (administration).</summary>
    Task<IReadOnlyList<TicketCost>> CostsAsync(CancellationToken cancellationToken = default);
}

public sealed record ReportTarget(Guid LeaseId, Guid UnitId, Guid PropertyId, string Unit, string Property);

public sealed class TicketReport
{
    [Required(ErrorMessage = "Bitte kurz angeben, worum es geht.")]
    [StringLength(200)]
    public string Title { get; set; } = "";

    public TicketCategory Category { get; set; } = TicketCategory.Other;

    public TicketUrgency Urgency { get; set; }

    [Required(ErrorMessage = "Bitte beschreiben Sie das Problem.")]
    [StringLength(4000)]
    public string Description { get; set; } = "";

    /// <summary>The problem is in the common areas (stairwell, cellar, garden), not in the own unit.</summary>
    public bool CommonArea { get; set; }

    /// <summary>Which of the tenant's leases (if there are several).</summary>
    public Guid? LeaseId { get; set; }

    [StringLength(200)]
    public string? Location { get; set; }

    [StringLength(500)]
    public string? AccessTimes { get; set; }
}

public sealed class AdminTicketInput
{
    [Required(ErrorMessage = "Bitte kurz angeben, worum es geht.")]
    [StringLength(200)]
    public string Title { get; set; } = "";

    public TicketCategory Category { get; set; } = TicketCategory.Other;

    public TicketUrgency Urgency { get; set; }

    [Required(ErrorMessage = "Bitte beschreiben Sie das Problem.")]
    [StringLength(4000)]
    public string Description { get; set; } = "";

    [Required(ErrorMessage = "Bitte das Objekt wählen.")]
    public Guid? PropertyId { get; set; }

    public Guid? UnitId { get; set; }

    [StringLength(200)]
    public string? Location { get; set; }

    public Guid? AssigneeId { get; set; }

    public DateOnly? AppointmentOn { get; set; }
}

public sealed record TicketRow(
    Guid Id,
    string Title,
    TicketCategory Category,
    TicketUrgency Urgency,
    TicketStatus Status,
    string Property,
    string? Unit,
    string? Reporter,
    string? Assignee,
    DateTimeOffset ReportedAt,
    DateOnly? AppointmentOn);

public sealed record TicketCommentView(Guid Id, string Author, string Text, bool IsInternal, DateTimeOffset At, IReadOnlyList<Guid> FileIds);

/// <param name="FileIds">Files of the report itself.</param>
/// <param name="CanComment">The account may write comments.</param>
/// <param name="CanSetStatus">Statuses the account may set.</param>
public sealed record TicketView(
    TicketRow Row,
    string Description,
    string? Location,
    string? AccessTimes,
    Guid? AssigneeId,
    Guid? ReceiptId,
    DateTimeOffset? ClosedAt,
    IReadOnlyList<Guid> FileIds,
    IReadOnlyList<TicketCommentView> Comments,
    bool CanComment,
    IReadOnlyList<TicketStatus> CanSetStatus);

public sealed record TicketCost(Guid TicketId, string Title, string Property, string? Unit, decimal Amount);
