using Elocaro.Domain.Common;

namespace Elocaro.Domain.Prospects;

public enum ProspectStatus
{
    Inquiry = 0,
    Viewing = 1,
    SelfDisclosure = 2,
    Accepted = 3,
    Declined = 4,
}

/// <summary>
/// Someone interested in a rental unit: where the contact stands (inquiry, viewing, self-disclosure, acceptance or rejection), the viewing date and notes.
/// It holds personal data of strangers, so it is kept only as long as needed: a rejected prospect carries the day its data has to be deleted.
/// </summary>
public sealed class Prospect : Entity
{
    public const int MaxName = 200;

    /// <summary>
    /// Months after a rejection until the personal data is deleted. The AGG gives rejected applicants two months to claim and three more to sue;
    /// the data protection authorities accept six months at most (DSK guidance 2.0, section D).
    /// </summary>
    public const int RetentionMonthsAfterRejection = 6;

    /// <summary>Months without any change until the data of a prospect without a decision is deleted (storage limitation, Art. 5 (1) e GDPR).</summary>
    public const int RetentionMonthsWithoutDecision = 6;

    private Prospect()
    {
        Name = "";
    }

    public Prospect(Guid rentalUnitId, string name)
    {
        RentalUnitId = rentalUnitId;
        Name = name;
        Validate();
    }

    /// <summary>The unit (or room) the prospect is interested in.</summary>
    public Guid RentalUnitId { get; private set; }

    public string Name { get; private set; }

    public string? Email { get; set; }

    public string? Phone { get; set; }

    public ProspectStatus Status { get; private set; }

    public DateOnly? ViewingOn { get; set; }

    public string? Notes { get; set; }

    /// <summary>Day of the acceptance or rejection.</summary>
    public DateOnly? DecidedOn { get; private set; }

    /// <summary>From this day the personal data is no longer kept (set with a rejection).</summary>
    public DateOnly? DeleteOn { get; private set; }

    /// <summary>The day the personal data is deleted: the set day, or six months after the last change if the prospect was never decided.</summary>
    public DateOnly DeletionDay => DeleteOn ?? GermanTime.Today(ModifiedAt ?? CreatedAt).AddMonths(RetentionMonthsWithoutDecision);

    /// <summary>The person created from the prospect when the prospect became a tenant.</summary>
    public Guid? PersonId { get; private set; }

    public void Rename(string name)
    {
        Name = name;
        Validate();
    }

    public void SetStatus(ProspectStatus status, DateOnly today, int retentionMonths)
    {
        if (Status == ProspectStatus.Accepted && status != ProspectStatus.Accepted && PersonId is not null)
        {
            throw new RuleViolationException("Der Interessent wurde schon als Mieter übernommen. Der Status lässt sich nicht mehr ändern.");
        }

        Status = status;
        DecidedOn = status is ProspectStatus.Accepted or ProspectStatus.Declined ? today : null;
        DeleteOn = status == ProspectStatus.Declined ? today.AddMonths(retentionMonths) : null;
    }

    /// <summary>Another prospect got the unit: the data is no longer needed and goes after the same period as a rejected one.</summary>
    public void ScheduleDeletion(DateOnly today) => DeleteOn ??= today.AddMonths(RetentionMonthsAfterRejection);

    /// <summary>The prospect became a tenant: the data now lives in the person record.</summary>
    public void AdoptAs(Guid personId, DateOnly today)
    {
        PersonId = personId;
        SetStatus(ProspectStatus.Accepted, today, 0);

        // The data now lives in the person record; the prospect (and its notes) goes after the same period as a rejected one.
        DeleteOn = today.AddMonths(RetentionMonthsAfterRejection);
    }

    private void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > MaxName)
        {
            throw new RuleViolationException("Bitte den Namen des Interessenten angeben (höchstens 200 Zeichen).");
        }
    }
}
