using MietHahn.Application.Common;
using MietHahn.Domain.Common;
using MietHahn.Domain.Properties;

namespace MietHahn.Application.Properties;

/// <summary>Pflege von Objekten, Einheiten und WG-Zimmern samt Flächen mit Gültigkeit.</summary>
public interface IPropertyService
{
    /// <param name="idsOnly">Nur diese Objekte (z. B. die eines Hausmeisters); <c>null</c> = alle.</param>
    Task<IReadOnlyList<PropertyOverview>> ListAsync(IReadOnlyCollection<Guid>? idsOnly = null, CancellationToken cancellationToken = default);

    Task<PropertyDetails?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result> CreateAsync(PropertyInput data, CancellationToken cancellationToken = default);

    Task<Result> UpdateAsync(Guid id, PropertyInput data, CancellationToken cancellationToken = default);

    /// <summary>Setzt das Foto des Hauses (Datei aus der Dateiablage) oder entfernt es.</summary>
    Task<Result> SetImageAsync(Guid id, Guid? fileId, CancellationToken cancellationToken = default);

    Task<Result> SetAreaAsync(Guid id, AreaInput input, CancellationToken cancellationToken = default);

    /// <summary>Nur ohne Einheiten möglich.</summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result> CreateUnitAsync(Guid propertyId, UnitInput data, CancellationToken cancellationToken = default);

    Task<UnitDetails?> GetUnitAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result> UpdateUnitAsync(Guid id, UnitInput data, CancellationToken cancellationToken = default);

    Task<Result> SetUnitAreaAsync(Guid id, AreaInput input, CancellationToken cancellationToken = default);

    /// <summary>Nur ohne Zimmer und Verträge möglich.</summary>
    Task<Result> DeleteUnitAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result> CreateRoomAsync(Guid unitId, RoomInput data, CancellationToken cancellationToken = default);

    Task<RoomDetails?> GetRoomAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result> UpdateRoomAsync(Guid id, RoomInput data, CancellationToken cancellationToken = default);

    Task<Result> SetRoomAreaAsync(Guid id, AreaInput input, CancellationToken cancellationToken = default);

    /// <summary>Nur ohne Verträge möglich.</summary>
    Task<Result> DeleteRoomAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed record PropertyOverview(Guid Id, string Name, string Address, int Units, int RentalUnits, int Rented, decimal? LivingArea)
{
    public int Vacancy => RentalUnits - Rented;
}

/// <summary>Ein Flächenstand; <see cref="SecondaryArea"/> je nach Ebene Gemeinschafts-, Heiz- bzw. Anteilsfläche.</summary>
public sealed record AreaReading(Period Validity, decimal Area, decimal SecondaryArea);

/// <summary>Was vermietet werden kann: eine Einheit ohne Zimmer oder ein WG-Zimmer.</summary>
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
