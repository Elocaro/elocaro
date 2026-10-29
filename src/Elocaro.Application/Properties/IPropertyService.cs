using Elocaro.Application.Common;
using Elocaro.Domain.Common;
using Elocaro.Domain.Properties;

namespace Elocaro.Application.Properties;

/// <summary>Maintenance of properties, units and shared-flat rooms including areas with validity.</summary>
public interface IPropertyService
{
    /// <param name="idsOnly">Only these properties (e.g. those of a caretaker); <c>null</c> = all.</param>
    Task<IReadOnlyList<PropertyOverview>> ListAsync(IReadOnlyCollection<Guid>? idsOnly = null, CancellationToken cancellationToken = default);

    Task<PropertyDetails?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result> CreateAsync(PropertyInput data, CancellationToken cancellationToken = default);

    Task<Result> UpdateAsync(Guid id, PropertyInput data, CancellationToken cancellationToken = default);

    /// <summary>Sets the photo of the house (file from the file store) or removes it.</summary>
    Task<Result> SetImageAsync(Guid id, Guid? fileId, CancellationToken cancellationToken = default);

    Task<Result> SetAreaAsync(Guid id, AreaInput input, CancellationToken cancellationToken = default);

    /// <summary>Only possible without units.</summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result> CreateUnitAsync(Guid propertyId, UnitInput data, CancellationToken cancellationToken = default);

    Task<UnitDetails?> GetUnitAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result> UpdateUnitAsync(Guid id, UnitInput data, CancellationToken cancellationToken = default);

    Task<Result> SetUnitAreaAsync(Guid id, AreaInput input, CancellationToken cancellationToken = default);

    /// <summary>Only possible without rooms and leases.</summary>
    Task<Result> DeleteUnitAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result> CreateRoomAsync(Guid unitId, RoomInput data, CancellationToken cancellationToken = default);

    Task<RoomDetails?> GetRoomAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result> UpdateRoomAsync(Guid id, RoomInput data, CancellationToken cancellationToken = default);

    Task<Result> SetRoomAreaAsync(Guid id, AreaInput input, CancellationToken cancellationToken = default);

    /// <summary>Sets the share of the common area of every room of the shared flat (living area of the flat minus the room areas).</summary>
    Task<Result> ApplyCommonAreaAsync(Guid unitId, CommonAreaMode mode, DateOnly from, CancellationToken cancellationToken = default);

    /// <summary>Only possible without leases.</summary>
    Task<Result> DeleteRoomAsync(Guid id, CancellationToken cancellationToken = default);
}

public enum CommonAreaMode
{
    /// <summary>Every room carries the same share.</summary>
    Equal = 0,

    /// <summary>The shares follow the room areas.</summary>
    ByRoomArea = 1,
}

/// <summary>Proposal for the share of the common area per room: living area of the flat − Σ room areas, distributed on the rooms.</summary>
public sealed record CommonAreaSuggestion(decimal UnitArea, decimal RoomAreas, decimal CommonArea, IReadOnlyList<CommonAreaShare> Shares, IReadOnlyList<string> Problems)
{
    public static CommonAreaSuggestion Of(decimal? unitArea, IReadOnlyList<(Guid Id, string Name, decimal? Area)> rooms, CommonAreaMode mode)
    {
        ArgumentNullException.ThrowIfNull(rooms);
        var problems = new List<string>();
        if (unitArea is not > 0m)
        {
            problems.Add("Für die Wohnung ist keine Wohnfläche hinterlegt.");
        }

        foreach (var room in rooms.Where(r => r.Area is not > 0m))
        {
            problems.Add($"{room.Name}: Die Zimmerfläche fehlt.");
        }

        var roomAreas = rooms.Sum(r => r.Area ?? 0m);
        var common = Math.Max(0m, (unitArea ?? 0m) - roomAreas);
        if (unitArea is { } total && roomAreas > total)
        {
            problems.Add($"Die Zimmer ({roomAreas:0.##} m²) sind zusammen größer als die Wohnung ({total:0.##} m²).");
        }

        var count = rooms.Count(r => r.Area is > 0m);
        var shares = new List<CommonAreaShare>();
        if (count > 0 && problems.Count == 0)
        {
            var rooms2 = rooms.Where(r => r.Area is > 0m).ToList();
            decimal assigned = 0;
            for (var i = 0; i < rooms2.Count; i++)
            {
                var share = i == rooms2.Count - 1
                    ? common - assigned
                    : decimal.Round(mode == CommonAreaMode.Equal ? common / count : common * rooms2[i].Area!.Value / roomAreas, 2, MidpointRounding.AwayFromZero);
                assigned += share;
                shares.Add(new CommonAreaShare(rooms2[i].Id, rooms2[i].Name, rooms2[i].Area!.Value, share));
            }
        }

        return new CommonAreaSuggestion(unitArea ?? 0m, roomAreas, common, shares, problems);
    }
}

public sealed record CommonAreaShare(Guid RoomId, string Name, decimal RoomArea, decimal Share);

public sealed record PropertyOverview(Guid Id, string Name, string Address, int Units, int RentalUnits, int Rented, decimal? LivingArea)
{
    public int Vacancy => RentalUnits - Rented;
}

/// <summary>An area state; <see cref="SecondaryArea"/> is common, heated or share area depending on the level.</summary>
public sealed record AreaReading(Period Validity, decimal Area, decimal SecondaryArea);

/// <summary>What can be rented: a unit without rooms or a shared-flat room.</summary>
public sealed record RentalUnitRow(Guid Id, string Name, bool IsRoom, decimal? Area, Occupancy? Occupancy);

public sealed record UnitRow(
    Guid Id,
    string Name,
    string? Location,
    UnitType Type,
    decimal? LivingArea,
    bool IsSharedFlat,
    IReadOnlyList<RentalUnitRow> RentalUnits);

public sealed record PropertyDetails(
    Guid Id,
    PropertyInput Data,
    string Address,
    Guid? ImageFileId,
    AreaReading? Area,
    IReadOnlyList<AreaReading> AreaHistory,
    IReadOnlyList<UnitRow> Units,
    IReadOnlyList<string> ValidationHints);

public sealed record RoomRow(Guid Id, string Name, Furnishing Furnishing, AreaReading? Area, Occupancy? Occupancy);

public sealed record UnitDetails(
    Guid Id,
    Guid PropertyId,
    string PropertyName,
    UnitInput Data,
    AreaReading? Area,
    IReadOnlyList<AreaReading> AreaHistory,
    IReadOnlyList<RoomRow> Room,
    Occupancy? Occupancy,
    IReadOnlyList<string> ValidationHints)
{
    public bool IsSharedFlat => Room.Count > 0;
}

public sealed record RoomDetails(
    Guid Id,
    Guid UnitId,
    string UnitName,
    Guid PropertyId,
    string PropertyName,
    RoomInput Data,
    AreaReading? Area,
    IReadOnlyList<AreaReading> AreaHistory,
    Occupancy? Occupancy);
