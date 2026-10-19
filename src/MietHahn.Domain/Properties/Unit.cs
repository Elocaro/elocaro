using MietHahn.Domain.Common;

namespace MietHahn.Domain.Properties;

public enum UnitType
{
    Apartment = 0,
    Commercial = 1,
    ParkingSpace = 2,
    Miscellaneous = 9,
}

/// <summary>
/// Wohnung (bzw. Gewerbe, Stellplatz) in einem <see cref="Property"/>. Ohne Zimmer ist die Einheit selbst vermietbar;
/// mit Zimmern (WG) werden nur die Zimmer einzeln vermietet.
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

    /// <summary>Nummer oder Name, z. B. „Wohnung 1“.</summary>
    public string Name { get; set; }

    /// <summary>Lage im Haus, z. B. „EG links“.</summary>
    public string? Location { get; set; }

    public UnitType Type { get; set; }

    /// <summary>Anzeigereihenfolge im Objekt.</summary>
    public int Order { get; set; }

    /// <summary>Anzahl Zimmer laut Exposé/Mietvertrag, z. B. 2,5.</summary>
    public decimal? RoomCount { get; set; }

    // Ausstattung (Mietspiegel-Merkmale)
    public string? Bathroom { get; set; }

    public bool Balcony { get; set; }

    public bool FittedKitchen { get; set; }

    /// <summary>z. B. Energieeffizienzklasse oder „KfW 70“.</summary>
    public string? EnergyStandard { get; set; }

    public string? Amenities { get; set; }

    public List<Room> Room { get; private set; } = [];

    public List<UnitArea> Areas { get; private set; } = [];

    /// <summary>WG: Die Zimmer werden einzeln vermietet.</summary>
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

    /// <summary>Wohn- und Heizfläche ab dem Stichtag. Ohne Angabe gilt die Heizfläche = Wohnfläche.</summary>
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

    /// <summary>Wohnfläche nach WoFlV in m² (Umlage nach Fläche).</summary>
    public decimal LivingArea { get; private set; }

    /// <summary>Beheizte Fläche in m² (Grundkosten der Heizkostenabrechnung), kann von der Wohnfläche abweichen.</summary>
    public decimal HeatedArea { get; private set; }

    public void EndValidity(DateOnly lastDay) => Validity = Validity.EndOn(lastDay);
}
