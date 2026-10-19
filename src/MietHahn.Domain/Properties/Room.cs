using MietHahn.Domain.Common;

namespace MietHahn.Domain.Properties;

public enum Furnishing
{
    Unfurnished = 0,
    PartlyFurnished = 1,
    Furnished = 2,
}

/// <summary>Individually rented room of a shared-flat <see cref="Unit"/>.</summary>
public sealed class Room : Entity
{
    private Room()
    {
        Name = "";
    }

    internal Room(Guid unitId, string name, int order)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        UnitId = unitId;
        Name = name;
        Order = order;
    }

    public Guid UnitId { get; private set; }

    /// <summary>E.g. "Zimmer 1" or "Zimmer Nord".</summary>
    public string Name { get; set; }

    public int Order { get; set; }

    public Furnishing Furnishing { get; set; }

    public string? Notes { get; set; }

    public List<RoomArea> Areas { get; private set; } = [];

    /// <summary>Room area and proportional common area of the shared flat (kitchen, bath, hallway) from the effective date.</summary>
    public RoomArea SetArea(DateOnly from, decimal area, decimal commonAreaShare = 0) =>
        TimeSeries.CarryForward(Areas, from, g => new RoomArea(Id, g, area, commonAreaShare));

    public RoomArea? AreaOn(DateOnly day) => TimeSeries.At(Areas, day);
}

public sealed class RoomArea : Entity, ITimeDependent
{
    private RoomArea()
    {
    }

    internal RoomArea(Guid roomId, Period validity, decimal area, decimal commonAreaShare)
    {
        AreaValue.Validate(area, "Zimmerfläche");
        AreaValue.Validate(commonAreaShare, "anteilige Gemeinschaftsfläche", allowNull: true);
        RoomId = roomId;
        Validity = validity;
        Area = area;
        CommonAreaShare = commonAreaShare;
    }

    public Guid RoomId { get; private set; }

    public Period Validity { get; private set; }

    public decimal Area { get; private set; }

    /// <summary>Share of the shared flat's common areas attributable to the room, in m².</summary>
    public decimal CommonAreaShare { get; private set; }

    /// <summary>Area for allocation by area: room plus share of the common areas.</summary>
    public decimal AllocationArea => Area + CommonAreaShare;

    public void EndValidity(DateOnly lastDay) => Validity = Validity.EndOn(lastDay);
}
