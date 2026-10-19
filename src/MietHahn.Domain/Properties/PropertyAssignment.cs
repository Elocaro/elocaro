using MietHahn.Domain.Common;

namespace MietHahn.Domain.Properties;

/// <summary>Ein Benutzerkonto (Hausmeister) betreut ein Objekt und sieht es im Servicebereich.</summary>
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
