using Elocaro.Domain.Common;

namespace Elocaro.Domain.Statements;

/// <summary>
/// The landlord does not want to be reminded of the operating cost statement of a property and calendar year any more
/// (e.g. the deadline has passed and no statement will be created). Only hides the dashboard note and the entry in the deadline list.
/// </summary>
public sealed class StatementReminderDismissal : Entity
{
    private StatementReminderDismissal()
    {
    }

    public StatementReminderDismissal(Guid propertyId, int year)
    {
        PropertyId = propertyId;
        Year = year;
    }

    public Guid PropertyId { get; private set; }

    public int Year { get; private set; }
}
