using Elocaro.Domain.Common;

namespace Elocaro.Domain.Properties;

/// <summary>A user account (caretaker) looks after a property and sees it in the service area.</summary>
public sealed class PropertyAssignment : Entity
{
    private PropertyAssignment()
    {
    }

    public PropertyAssignment(Guid propertyId, Guid userId)
    {
        PropertyId = propertyId;
        UserId = userId;
    }

    public Guid PropertyId { get; private set; }

    public Guid UserId { get; private set; }
}
