using MietHahn.Domain.Common;

namespace MietHahn.Domain.Properties;

public enum Furnishing
{
    Unfurnished = 0,
    PartlyFurnished = 1,
    Furnished = 2,
}

/// <summary>Einzeln vermietetes Zimmer einer WG-<see cref="Unit"/>.</summary>
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

    /// <summary>z. B. „Zimmer 1“ oder „Zimmer Nord“.</summary>
    public string Name { get; set; }

    public int Order { get; set; }

    public Furnishing Furnishing { get; set; }

    public string? Notes { get; set; }

    public List<RoomArea> Areas { get; private set; } = [];

    /// <summary>Zimmerfläche und anteilige Gemeinschaftsfläche der WG (Küche, Bad, Flur) ab dem Stichtag.</summary>
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

    /// <summary>Auf das Zimmer entfallender Teil der WG-Gemeinschaftsflächen in m².</summary>
    public decimal CommonAreaShare { get; private set; }

    /// <summary>Fläche für Umlagen nach Fläche: Zimmer + Anteil an Gemeinschaftsflächen.</summary>
    public decimal AllocationArea => Area + CommonAreaShare;

    public void EndValidity(DateOnly lastDay) => Validity = Validity.EndOn(lastDay);
}
