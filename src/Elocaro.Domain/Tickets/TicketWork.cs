using System.Security.Cryptography;
using System.Text;
using Elocaro.Domain.Common;

namespace Elocaro.Domain.Tickets;

/// <summary>Working time and material that an assignee has booked on a ticket.</summary>
public sealed class TicketWork : Entity
{
    private TicketWork()
    {
        AuthorName = "";
    }

    public TicketWork(Guid ticketId, DateOnly workedOn, int minutes, string? material, string? note, Guid? authorUserId, string authorName)
    {
        if (minutes is < 0 or > 24 * 60)
        {
            throw new RuleViolationException("Die Arbeitszeit muss zwischen 0 und 24 Stunden liegen.");
        }

        if (minutes == 0 && string.IsNullOrWhiteSpace(material) && string.IsNullOrWhiteSpace(note))
        {
            throw new RuleViolationException("Bitte Arbeitszeit, Material oder eine Bemerkung angeben.");
        }

        TicketId = ticketId;
        WorkedOn = workedOn;
        Minutes = minutes;
        Material = string.IsNullOrWhiteSpace(material) ? null : material.Trim();
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        AuthorUserId = authorUserId;
        AuthorName = authorName;
    }

    public Guid TicketId { get; private set; }

    public DateOnly WorkedOn { get; private set; }

    public int Minutes { get; private set; }

    public string? Material { get; private set; }

    public string? Note { get; private set; }

    public Guid? AuthorUserId { get; private set; }

    public string AuthorName { get; private set; }
}

/// <summary>An invoice (file) uploaded for a ticket by the assignee; the administration turns it into a receipt.</summary>
public sealed class TicketInvoice : Entity
{
    private TicketInvoice()
    {
        UploadedBy = "";
    }

    public TicketInvoice(Guid ticketId, Guid fileId, string uploadedBy)
    {
        TicketId = ticketId;
        FileId = fileId;
        UploadedBy = uploadedBy;
    }

    public Guid TicketId { get; private set; }

    public Guid FileId { get; private set; }

    public string UploadedBy { get; private set; }
}

/// <summary>
/// Time-limited access to exactly one ticket for a tradesperson without an account. Only the hash of the secret is stored.
/// The link stops working when it expires, is revoked or the ticket is closed.
/// </summary>
public sealed class TicketAccessLink : Entity
{
    public const int DefaultDays = 14;
    public const int MaxDays = 60;

    private TicketAccessLink()
    {
        TokenHash = "";
        Recipient = "";
    }

    public TicketAccessLink(Guid ticketId, string tokenHash, string recipient, DateTimeOffset expiresAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipient);
        TicketId = ticketId;
        TokenHash = tokenHash;
        Recipient = recipient.Trim();
        ExpiresAt = expiresAt;
    }

    public Guid TicketId { get; private set; }

    public string TokenHash { get; private set; }

    /// <summary>Who the link was given to (company or name), for the administration and the author line.</summary>
    public string Recipient { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public bool IsValid(DateTimeOffset now) => RevokedAt is null && now < ExpiresAt;

    public void Revoke(DateTimeOffset at) => RevokedAt ??= at;

    /// <summary>A fresh unguessable secret (256 bits, URL-safe).</summary>
    public static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

public enum CareInterval
{
    Daily = 0,
    Weekly = 1,
    Monthly = 2,
    Quarterly = 3,
    Yearly = 4,
}

/// <summary>A recurring caretaker duty at a property (stairwell cleaning, winter service, bins) that is ticked off as proof.</summary>
public sealed class CareRoutine : Entity
{
    private CareRoutine()
    {
        Title = "";
    }

    public CareRoutine(Guid propertyId, string title, CareInterval interval, string? description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        PropertyId = propertyId;
        Title = title.Trim();
        Interval = interval;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Active = true;
    }

    public Guid PropertyId { get; private set; }

    public string Title { get; private set; }

    public string? Description { get; private set; }

    public CareInterval Interval { get; private set; }

    public bool Active { get; private set; }

    public void Deactivate() => Active = false;

    /// <summary>First day on which the routine is due again after <paramref name="lastDone"/>.</summary>
    public DateOnly NextDue(DateOnly lastDone) => Interval switch
    {
        CareInterval.Daily => lastDone.AddDays(1),
        CareInterval.Weekly => lastDone.AddDays(7),
        CareInterval.Monthly => lastDone.AddMonths(1),
        CareInterval.Quarterly => lastDone.AddMonths(3),
        _ => lastDone.AddYears(1),
    };

    public bool IsDue(DateOnly? lastDone, DateOnly today) => lastDone is null || NextDue(lastDone.Value) <= today;

    /// <summary>
    /// The due day has passed without a tick. A routine never ticked counts from the day it was created, so a new one does not
    /// raise an alarm right away.
    /// </summary>
    public bool IsOverdue(DateOnly? lastDone, DateOnly createdOn, DateOnly today) => NextDue(lastDone ?? createdOn) < today;
}

/// <summary>One tick of a routine: done on a day by a person (proof for the caretaker apportionment and § 35a).</summary>
public sealed class CareCheck : Entity
{
    private CareCheck()
    {
        DoneBy = "";
    }

    public CareCheck(Guid routineId, DateOnly doneOn, Guid? userId, string doneBy, string? note, Guid? fileId = null)
    {
        FileId = fileId;
        RoutineId = routineId;
        DoneOn = doneOn;
        UserId = userId;
        DoneBy = doneBy;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }

    public Guid RoutineId { get; private set; }

    public DateOnly DoneOn { get; private set; }

    public Guid? UserId { get; private set; }

    public string DoneBy { get; private set; }

    public string? Note { get; private set; }

    /// <summary>Optional photo as additional proof.</summary>
    public Guid? FileId { get; private set; }
}
