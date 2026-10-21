using MietHahn.Domain.Common;

namespace MietHahn.Domain.Tickets;

public enum TicketCategory
{
    Heating = 0,
    Water = 1,
    Electrical = 2,
    WindowsDoors = 3,
    CommonAreas = 4,
    Other = 9,
}

public enum TicketUrgency
{
    Normal = 0,
    High = 1,
}

/// <summary>New → in review → commissioned → in progress → done → closed.</summary>
public enum TicketStatus
{
    New = 0,
    InReview = 1,
    Commissioned = 2,
    InProgress = 3,
    Done = 4,
    Closed = 5,
}

/// <summary>
/// A damage report or task: reported by a tenant or created by the administration (e.g. from a maintenance task),
/// assigned to a caretaker or tradesperson. Comments are public (tenant and assignee see them) or internal.
/// </summary>
public sealed class Ticket : Entity
{
    public const int MaxPhotos = 5;

    private Ticket()
    {
        Title = "";
        Description = "";
    }

    public Ticket(string title, string description, TicketCategory category, TicketUrgency urgency, Guid propertyId, Guid? unitId, Guid? leaseId, Guid? reporterId, DateTimeOffset reportedAt)
    {
        Title = title.Trim();
        Description = description.Trim();
        Category = category;
        Urgency = urgency;
        PropertyId = propertyId;
        UnitId = unitId;
        LeaseId = leaseId;
        ReporterId = reporterId;
        ReportedAt = reportedAt;
        Validate();
    }

    public string Title { get; private set; }

    public string Description { get; private set; }

    public TicketCategory Category { get; private set; }

    public TicketUrgency Urgency { get; set; }

    public Guid PropertyId { get; private set; }

    /// <summary><c>null</c> = common areas / whole property.</summary>
    public Guid? UnitId { get; private set; }

    /// <summary>Where exactly, e.g. "Bad", "Treppenhaus 2. OG".</summary>
    public string? Location { get; set; }

    public Guid? LeaseId { get; private set; }

    /// <summary>The tenant (person) who reported it; <c>null</c> if created by the administration.</summary>
    public Guid? ReporterId { get; private set; }

    /// <summary>Times at which the tenant can be reached or let someone in.</summary>
    public string? AccessTimes { get; set; }

    public DateTimeOffset ReportedAt { get; private set; }

    public TicketStatus Status { get; private set; }

    /// <summary>Caretaker or tradesperson (person with a user account) who is working on it.</summary>
    public Guid? AssigneeId { get; private set; }

    public DateOnly? AppointmentOn { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    /// <summary>Invoice for the work (receipt), so that costs per unit can be analysed.</summary>
    public Guid? ReceiptId { get; set; }

    public bool IsOpen => Status != TicketStatus.Closed;

    public void SetStatus(TicketStatus status, DateTimeOffset timestamp)
    {
        if (Status == TicketStatus.Closed && status != TicketStatus.Closed)
        {
            throw new RuleViolationException("Ein geschlossenes Ticket lässt sich nicht mehr ändern. Bitte ein neues Ticket anlegen.");
        }

        Status = status;
        ClosedAt = status == TicketStatus.Closed ? timestamp : null;
    }

    public void Assign(Guid? assigneeId, DateOnly? appointmentOn)
    {
        if (Status == TicketStatus.Closed)
        {
            throw new RuleViolationException("Ein geschlossenes Ticket lässt sich nicht mehr ändern.");
        }

        AssigneeId = assigneeId;
        AppointmentOn = appointmentOn;
    }

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Title);
        if (string.IsNullOrWhiteSpace(Description))
        {
            throw new RuleViolationException("Bitte beschreiben Sie das Problem.");
        }
    }
}

public sealed class TicketComment : Entity
{
    private TicketComment()
    {
        Text = "";
        AuthorName = "";
    }

    public TicketComment(Guid ticketId, string text, bool isInternal, Guid? authorUserId, string authorName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        TicketId = ticketId;
        Text = text.Trim();
        IsInternal = isInternal;
        AuthorUserId = authorUserId;
        AuthorName = authorName;
    }

    public Guid TicketId { get; private set; }

    public string Text { get; private set; }

    /// <summary>Visible to the administration only.</summary>
    public bool IsInternal { get; private set; }

    public Guid? AuthorUserId { get; private set; }

    public string AuthorName { get; private set; }
}

/// <summary>A photo (or other file) of the ticket or of one of its comments.</summary>
public sealed class TicketPhoto : Entity
{
    private TicketPhoto()
    {
    }

    public TicketPhoto(Guid ticketId, Guid fileId, Guid? commentId)
    {
        TicketId = ticketId;
        FileId = fileId;
        CommentId = commentId;
    }

    public Guid TicketId { get; private set; }

    public Guid FileId { get; private set; }

    /// <summary><c>null</c> = belongs to the report itself.</summary>
    public Guid? CommentId { get; private set; }
}
