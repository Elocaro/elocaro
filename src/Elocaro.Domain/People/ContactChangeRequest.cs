using Elocaro.Domain.Common;

namespace Elocaro.Domain.People;

public enum ContactChangeStatus
{
    Open = 0,
    Approved = 1,
    Rejected = 2,
}

/// <summary>
/// A tenant's request to change their contact data in the portal. It changes nothing until an administrator
/// approves it; the new values then replace the ones of the person.
/// </summary>
public sealed class ContactChangeRequest : Entity
{
    private ContactChangeRequest()
    {
    }

    public ContactChangeRequest(Guid personId, string? email, string? phone, string? mobile, DateTimeOffset requestedAt)
    {
        PersonId = personId;
        Email = Clean(email);
        Phone = Clean(phone);
        Mobile = Clean(mobile);
        RequestedAt = requestedAt;
        if (Email is null && Phone is null && Mobile is null)
        {
            throw new RuleViolationException("Bitte mindestens einen Kontaktweg angeben.");
        }
    }

    public Guid PersonId { get; private set; }

    public string? Email { get; private set; }

    public string? Phone { get; private set; }

    public string? Mobile { get; private set; }

    public DateTimeOffset RequestedAt { get; private set; }

    public ContactChangeStatus Status { get; private set; }

    public DateTimeOffset? DecidedAt { get; private set; }

    public string? DecisionNote { get; private set; }

    /// <summary>Copies the requested values to the person and closes the request.</summary>
    public void Approve(Person person, DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(person);
        EnsureOpen();
        person.Email = Email;
        person.Phone = Phone;
        person.Mobile = Mobile;
        person.Validate();
        Status = ContactChangeStatus.Approved;
        DecidedAt = timestamp;
    }

    public void Reject(string? note, DateTimeOffset timestamp)
    {
        EnsureOpen();
        Status = ContactChangeStatus.Rejected;
        DecidedAt = timestamp;
        DecisionNote = Clean(note);
    }

    private void EnsureOpen()
    {
        if (Status != ContactChangeStatus.Open)
        {
            throw new RuleViolationException("Der Antrag wurde bereits bearbeitet.");
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
