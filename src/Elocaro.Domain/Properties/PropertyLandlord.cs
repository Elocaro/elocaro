using Elocaro.Domain.Common;

namespace Elocaro.Domain.Properties;

/// <summary>
/// From a day on, documents of a property carry the sender details of this landlord profile (change of owner or manager).
/// The profile chosen on the property applies until the first entry; later entries replace it from their day.
/// </summary>
public sealed class PropertyLandlord : Entity
{
    private PropertyLandlord()
    {
    }

    public PropertyLandlord(Guid propertyId, Guid landlordProfileId, DateOnly validFrom)
    {
        PropertyId = propertyId;
        LandlordProfileId = landlordProfileId;
        ValidFrom = validFrom;
    }

    public Guid PropertyId { get; private set; }

    public Guid LandlordProfileId { get; private set; }

    public DateOnly ValidFrom { get; private set; }

    public void Change(Guid landlordProfileId, DateOnly validFrom)
    {
        LandlordProfileId = landlordProfileId;
        ValidFrom = validFrom;
    }
}
