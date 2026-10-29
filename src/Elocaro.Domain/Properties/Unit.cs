using Elocaro.Domain.Common;
using Elocaro.Domain.OperatingCosts;

namespace Elocaro.Domain.Properties;

public enum UnitType
{
    Apartment = 0,
    Commercial = 1,
    ParkingSpace = 2,
    Miscellaneous = 9,
}

/// <summary>
/// Apartment (or commercial unit, parking space) in a <see cref="Property"/>. Without rooms the unit itself can be rented;
/// with rooms (shared flat) only the rooms are rented individually.
/// </summary>
public sealed class Unit : Entity
{
    private Unit()
    {
        Name = "";
    }

    internal Unit(Guid propertyId, string name, UnitType type, int order)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        PropertyId = propertyId;
        Name = name;
        Type = type;
        Order = order;
    }

    public Guid PropertyId { get; private set; }

    /// <summary>Number or name, e.g. "Wohnung 1".</summary>
    public string Name { get; set; }

    /// <summary>Location in the building, e.g. "EG links".</summary>
    public string? Location { get; set; }

    public UnitType Type { get; set; }

    /// <summary>Display order within the property.</summary>
    public int Order { get; set; }

    /// <summary>Number of rooms according to listing/lease, e.g. 2.5.</summary>
    public decimal? RoomCount { get; set; }

    // Features (rent index criteria)
    public string? Bathroom { get; set; }

    public bool Balcony { get; set; }

    public bool FittedKitchen { get; set; }

    /// <summary>E.g. energy efficiency class or "KfW 70".</summary>
    public string? EnergyStandard { get; set; }

    public string? Amenities { get; set; }

    /// <summary>
    /// Cost types (keys from the cost type catalog, e.g. waste) that the tenants of the unit pay themselves directly to the supplier:
    /// the unit does not take part in their allocation, neither tenants nor landlord carry a share for it.
    /// </summary>
    public List<string> SelfPaidCostTypes { get; set; } = [];

    /// <summary>Shared flat: flat rate for the rooms (no statement, internal comparison) or distribution of the costs over the rooms.</summary>
    public SharedFlatBilling SharedFlatBilling { get; set; }

    public List<Room> Room { get; private set; } = [];

    public List<UnitArea> Areas { get; private set; } = [];

    /// <summary>Shared flat: the rooms are rented individually.</summary>
    public bool IsSharedFlat => Room.Count > 0;

    public bool IsSelfRentable => !IsSharedFlat;

    public Room AddRoom(string name)
    {
        if (Room.Any(z => string.Equals(z.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            throw new RuleViolationException($"In der Einheit gibt es bereits ein Zimmer „{name.Trim()}“.");
        }

        var room = new Room(Id, name.Trim(), Room.Count == 0 ? 1 : Room.Max(z => z.Order) + 1);
        Room.Add(room);
        return room;
    }

    /// <summary>Living and heated area from the effective date. If not specified, the heated area equals the living area.</summary>
    public UnitArea SetArea(DateOnly from, decimal livingArea, decimal? heatedArea = null) =>
        TimeSeries.CarryForward(Areas, from, g => new UnitArea(Id, g, livingArea, heatedArea ?? livingArea));

    public UnitArea? AreaOn(DateOnly day) => TimeSeries.At(Areas, day);

    public IEnumerable<string> ValidationHints(DateOnly day)
    {
        var area = AreaOn(day);
        if (area is null)
        {
            yield return $"{Name}: Keine Wohnfläche zum {day:dd.MM.yyyy} hinterlegt.";
            yield break;
        }

        var room = Room.Sum(z => (z.AreaOn(day)?.Area ?? 0) + (z.AreaOn(day)?.CommonAreaShare ?? 0));
        if (IsSharedFlat && room > area.LivingArea)
        {
            yield return $"{Name}: Die Zimmerflächen inkl. Anteilen an Gemeinschaftsflächen ({Property.SquareMeters(room)}) " +
                $"sind größer als die Wohnfläche ({Property.SquareMeters(area.LivingArea)}).";
        }
    }
}

public sealed class UnitArea : Entity, ITimeDependent
{
    private UnitArea()
    {
    }

    internal UnitArea(Guid unitId, Period validity, decimal livingArea, decimal heatedArea)
    {
        AreaValue.Validate(livingArea, "Wohnfläche");
        AreaValue.Validate(heatedArea, "Heizfläche", allowNull: true);
        UnitId = unitId;
        Validity = validity;
        LivingArea = livingArea;
        HeatedArea = heatedArea;
    }

    public Guid UnitId { get; private set; }

    public Period Validity { get; private set; }

    /// <summary>Living area according to WoFlV in m² (allocation by area).</summary>
    public decimal LivingArea { get; private set; }

    /// <summary>Heated area in m² (basic costs of the heating cost statement); may differ from the living area.</summary>
    public decimal HeatedArea { get; private set; }

    public void EndValidity(DateOnly lastDay) => Validity = Validity.EndOn(lastDay);
}
