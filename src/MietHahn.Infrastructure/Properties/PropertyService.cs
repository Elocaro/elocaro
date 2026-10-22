using Microsoft.EntityFrameworkCore;
using MietHahn.Application.Common;
using MietHahn.Application.Properties;
using MietHahn.Domain.Common;
using MietHahn.Domain.Files;
using MietHahn.Domain.Properties;
using MietHahn.Infrastructure.Common;
using MietHahn.Infrastructure.Persistence;

namespace MietHahn.Infrastructure.Properties;

internal sealed class PropertyService(MietHahnDbContext db, IOccupancyLookup occupancies, TimeProvider clock) : IPropertyService
{
    private DateOnly Today => GermanTime.Today(clock.GetUtcNow());

    public async Task<IReadOnlyList<PropertyOverview>> ListAsync(IReadOnlyCollection<Guid>? idsOnly = null, CancellationToken cancellationToken = default)
    {
        var query = WithAll();
        if (idsOnly is not null)
        {
            query = query.Where(o => idsOnly.Contains(o.Id));
        }

        var properties = await query.OrderBy(o => o.Name).ToListAsync(cancellationToken);
        var rentalUnits = properties.SelectMany(RentalUnits).ToList();
        var occupied = await occupancies.AtAsync(rentalUnits.Select(m => m.Id).ToList(), Today, cancellationToken);
        return properties.Select(o =>
        {
            var ids = RentalUnits(o).Select(m => m.Id).ToList();
            var livingArea = o.Units.Select(e => e.AreaOn(Today)?.LivingArea).Where(f => f is not null).Sum();
            return new PropertyOverview(o.Id, o.Name, o.Address, o.Units.Count, ids.Count, ids.Count(occupied.ContainsKey), livingArea == 0 ? null : livingArea);
        }).ToList();
    }

    public async Task<PropertyDetails?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var property = await WithAll().SingleOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (property is null)
        {
            return null;
        }

        var occupied = await occupancies.AtAsync(RentalUnits(property).Select(m => m.Id).ToList(), Today, cancellationToken);
        var units = property.Units.OrderBy(e => e.Order).Select(e => new UnitRow(
            e.Id,
            e.Name,
            e.Location,
            e.Type,
            e.AreaOn(Today)?.LivingArea,
            e.IsSharedFlat,
            e.IsSharedFlat
                ? e.Room.OrderBy(z => z.Order).Select(z => new RentalUnitRow(z.Id, z.Name, true, z.AreaOn(Today)?.Area, occupied.GetValueOrDefault(z.Id))).ToList()
                : [new RentalUnitRow(e.Id, e.Name, false, e.AreaOn(Today)?.LivingArea, occupied.GetValueOrDefault(e.Id))])).ToList();

        return new PropertyDetails(
            property.Id,
            Data(property),
            property.Address,
            property.ImageFileId,
            Entry(property.AreaOn(Today)),
            History(property.Areas, f => new AreaReading(f.Validity, f.TotalArea, f.CommonArea)),
            units,
            property.ValidationHints(Today));
    }

    public Task<Result> CreateAsync(PropertyInput data, CancellationToken cancellationToken = default) =>
        ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(data);
            var property = new Property(data.Name.Trim(), data.Street.Trim(), data.PostalCode.Trim(), data.City.Trim());
            Apply(property, data);
            db.Add(property);
            await db.SaveChangesAsync(cancellationToken);
            return property.Id;
        });

    public Task<Result> UpdateAsync(Guid id, PropertyInput data, CancellationToken cancellationToken = default) =>
        WithPropertyAsync(id, property =>
        {
            ArgumentNullException.ThrowIfNull(data);
            Apply(property, data);
        }, cancellationToken);

    public async Task<Result> SetImageAsync(Guid id, Guid? fileId, CancellationToken cancellationToken = default)
    {
        if (fileId is { } file && !await db.Set<StoredFile>().AnyAsync(d => d.Id == file && d.MimeType.StartsWith("image/"), cancellationToken))
        {
            return Result.Failed("Das Foto muss ein Bild sein.");
        }

        return await WithPropertyAsync(id, property => property.ImageFileId = fileId, cancellationToken);
    }

    public Task<Result> SetAreaAsync(Guid id, AreaInput input, CancellationToken cancellationToken = default) =>
        WithPropertyAsync(id, property => property.SetArea(From(input), Required(input.Area), input.SecondaryArea ?? 0), cancellationToken);

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var property = await WithAll().SingleOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (property is null)
        {
            return NotFound;
        }

        if (property.Units.Count > 0)
        {
            return Result.Failed("Das Objekt hat noch Einheiten. Bitte zuerst die Einheiten löschen.");
        }

        db.RemoveRange(await db.Set<PropertyAssignment>().Where(z => z.PropertyId == id).ToListAsync(cancellationToken));
        db.Remove(property);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Ok(id);
    }

    public async Task<Result> CreateUnitAsync(Guid propertyId, UnitInput data, CancellationToken cancellationToken = default)
    {
        Guid? newItem = null;
        var result = await WithPropertyAsync(propertyId, property =>
        {
            ArgumentNullException.ThrowIfNull(data);
            var unit = property.AddUnit(data.Name, data.Type);
            Apply(unit, data);
            newItem = unit.Id;
        }, cancellationToken);
        return result.Succeeded ? Result.Ok(newItem) : result;
    }

    public async Task<UnitDetails?> GetUnitAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var property = await WithAll().SingleOrDefaultAsync(o => o.Units.Any(e => e.Id == id), cancellationToken);
        var unit = property?.Units.Single(e => e.Id == id);
        if (property is null || unit is null)
        {
            return null;
        }

        var occupied = await occupancies.AtAsync([unit.Id, .. unit.Room.Select(z => z.Id)], Today, cancellationToken);
        return new UnitDetails(
            unit.Id,
            property.Id,
            property.Name,
            Data(unit),
            Entry(unit.AreaOn(Today)),
            History(unit.Areas, f => new AreaReading(f.Validity, f.LivingArea, f.HeatedArea)),
            unit.Room.OrderBy(z => z.Order).Select(z => new RoomRow(z.Id, z.Name, z.Furnishing, Entry(z.AreaOn(Today)), occupied.GetValueOrDefault(z.Id))).ToList(),
            occupied.GetValueOrDefault(unit.Id),
            unit.ValidationHints(Today).ToList());
    }

    public Task<Result> UpdateUnitAsync(Guid id, UnitInput data, CancellationToken cancellationToken = default) =>
        WithUnitAsync(id, (property, unit) =>
        {
            ArgumentNullException.ThrowIfNull(data);
            if (property.Units.Any(e => e.Id != id && string.Equals(e.Name, data.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                throw new RuleViolationException($"Im Objekt gibt es bereits eine Einheit „{data.Name.Trim()}“.");
            }

            Apply(unit, data);
        }, cancellationToken);

    public Task<Result> SetUnitAreaAsync(Guid id, AreaInput input, CancellationToken cancellationToken = default) =>
        WithUnitAsync(id, (_, unit) => unit.SetArea(From(input), Required(input.Area), input.SecondaryArea), cancellationToken);

    public async Task<Result> DeleteUnitAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (await HasLeasesAsync(id, cancellationToken))
        {
            return Result.Failed("Für die Einheit gibt es Mietverträge. Sie kann nicht gelöscht werden.");
        }

        return await WithUnitAsync(id, (property, unit) =>
        {
            if (unit.IsSharedFlat)
            {
                throw new RuleViolationException("Die Einheit hat noch Zimmer. Bitte zuerst die Zimmer löschen.");
            }

            property.Units.Remove(unit);
        }, cancellationToken);
    }

    public async Task<Result> CreateRoomAsync(Guid unitId, RoomInput data, CancellationToken cancellationToken = default)
    {
        if (await HasLeasesAsync(unitId, cancellationToken))
        {
            return Result.Failed("Die Einheit ist als Ganzes vermietet. Zimmer sind nur für WGs ohne Vertrag über die ganze Einheit möglich.");
        }

        Guid? newItem = null;
        var result = await WithUnitAsync(unitId, (_, unit) =>
        {
            ArgumentNullException.ThrowIfNull(data);
            var room = unit.AddRoom(data.Name);
            Apply(room, data);
            newItem = room.Id;
        }, cancellationToken);
        return result.Succeeded ? Result.Ok(newItem) : result;
    }

    public async Task<RoomDetails?> GetRoomAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var property = await WithAll().SingleOrDefaultAsync(o => o.Units.Any(e => e.Room.Any(z => z.Id == id)), cancellationToken);
        var unit = property?.Units.Single(e => e.Room.Any(z => z.Id == id));
        var room = unit?.Room.Single(z => z.Id == id);
        if (property is null || unit is null || room is null)
        {
            return null;
        }

        var occupied = await occupancies.AtAsync([room.Id], Today, cancellationToken);
        return new RoomDetails(
            room.Id,
            unit.Id,
            unit.Name,
            property.Id,
            property.Name,
            new RoomInput { Name = room.Name, Furnishing = room.Furnishing, FurnishingList = room.FurnishingList, Notes = room.Notes },
            Entry(room.AreaOn(Today)),
            History(room.Areas, f => new AreaReading(f.Validity, f.Area, f.CommonAreaShare)),
            occupied.GetValueOrDefault(room.Id));
    }

    public Task<Result> UpdateRoomAsync(Guid id, RoomInput data, CancellationToken cancellationToken = default) =>
        WithRoomAsync(id, (unit, room) =>
        {
            ArgumentNullException.ThrowIfNull(data);
            if (unit.Room.Any(z => z.Id != id && string.Equals(z.Name, data.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                throw new RuleViolationException($"In der Einheit gibt es bereits ein Zimmer „{data.Name.Trim()}“.");
            }

            Apply(room, data);
        }, cancellationToken);

    public Task<Result> SetRoomAreaAsync(Guid id, AreaInput input, CancellationToken cancellationToken = default) =>
        WithRoomAsync(id, (_, room) => room.SetArea(From(input), Required(input.Area), input.SecondaryArea ?? 0), cancellationToken);

    public async Task<Result> DeleteRoomAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (await HasLeasesAsync(id, cancellationToken))
        {
            return Result.Failed("Für das Zimmer gibt es Mietverträge. Es kann nicht gelöscht werden.");
        }

        return await WithRoomAsync(id, (unit, room) =>
        {
            unit.Room.Remove(room);
        }, cancellationToken);
    }

    private Task<bool> HasLeasesAsync(Guid rentalUnitId, CancellationToken cancellationToken) =>
        occupancies.HasLeasesAsync(rentalUnitId, cancellationToken);

    private IQueryable<Property> WithAll() =>
        db.Set<Property>()
            .Include(o => o.Areas)
            .Include(o => o.Units).ThenInclude(e => e.Areas)
            .Include(o => o.Units).ThenInclude(e => e.Room).ThenInclude(z => z.Areas)
            .AsSplitQuery();

    private static IEnumerable<(Guid Id, string Name)> RentalUnits(Property property) =>
        property.Units.SelectMany(e => e.IsSharedFlat ? e.Room.Select(z => (z.Id, z.Name)) : [(e.Id, e.Name)]);

    private Task<Result> WithPropertyAsync(Guid id, Action<Property> action, CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            var property = await WithAll().SingleOrDefaultAsync(o => o.Id == id, cancellationToken) ?? throw new NotFoundException();
            action(property);
            property.Validate();
            await db.SaveChangesAsync(cancellationToken);
            return id;
        });

    private Task<Result> WithUnitAsync(Guid id, Action<Property, Unit> action, CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            var property = await WithAll().SingleOrDefaultAsync(o => o.Units.Any(e => e.Id == id), cancellationToken) ?? throw new NotFoundException();
            action(property, property.Units.Single(e => e.Id == id));
            await db.SaveChangesAsync(cancellationToken);
            return id;
        });

    private Task<Result> WithRoomAsync(Guid id, Action<Unit, Room> action, CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            var property = await WithAll().SingleOrDefaultAsync(o => o.Units.Any(e => e.Room.Any(z => z.Id == id)), cancellationToken) ?? throw new NotFoundException();
            var unit = property.Units.Single(e => e.Room.Any(z => z.Id == id));
            action(unit, unit.Room.Single(z => z.Id == id));
            await db.SaveChangesAsync(cancellationToken);
            return id;
        });

    private static Task<Result> ExecuteAsync(Func<Task<Guid>> command) => Command.ExecuteAsync(command);

    private static Result NotFound => Command.NotFound;

    private static DateOnly From(AreaInput input) =>
        input.From ?? throw new RuleViolationException("Bitte angeben, ab wann die Fläche gilt.");

    private static decimal Required(decimal? value) => value ?? throw new RuleViolationException("Bitte die Fläche angeben.");

    private static AreaReading? Entry(PropertyArea? f) => f is null ? null : new(f.Validity, f.TotalArea, f.CommonArea);

    private static AreaReading? Entry(UnitArea? f) => f is null ? null : new(f.Validity, f.LivingArea, f.HeatedArea);

    private static AreaReading? Entry(RoomArea? f) => f is null ? null : new(f.Validity, f.Area, f.CommonAreaShare);

    private static List<AreaReading> History<T>(IEnumerable<T> readings, Func<T, AreaReading> map)
        where T : ITimeDependent =>
        readings.OrderByDescending(s => s.Validity.From).Select(map).ToList();

    private static PropertyInput Data(Property o) => new()
    {
        Name = o.Name,
        Street = o.Street,
        PostalCode = o.PostalCode,
        City = o.City,
        YearBuilt = o.YearBuilt,
        CadastralDistrict = o.CadastralDistrict,
        ParcelNumber = o.ParcelNumber,
        PropertyTaxReference = o.PropertyTaxReference,
        HeatingType = o.HeatingType,
        EnergySource = o.EnergySource,
        RentCapPercent = o.RentCapPercent,
        Notes = o.Notes,
        LandlordProfileId = o.LandlordProfileId,
    };

    private static UnitInput Data(Unit e) => new()
    {
        Name = e.Name,
        Location = e.Location,
        Type = e.Type,
        RoomCount = e.RoomCount,
        Bathroom = e.Bathroom,
        Balcony = e.Balcony,
        FittedKitchen = e.FittedKitchen,
        EnergyStandard = e.EnergyStandard,
        Amenities = e.Amenities,
    };

    private static void Apply(Property o, PropertyInput d)
    {
        o.Name = d.Name.Trim();
        o.Street = d.Street.Trim();
        o.PostalCode = d.PostalCode.Trim();
        o.City = d.City.Trim();
        o.YearBuilt = d.YearBuilt;
        o.CadastralDistrict = TrimToNull(d.CadastralDistrict);
        o.ParcelNumber = TrimToNull(d.ParcelNumber);
        o.PropertyTaxReference = TrimToNull(d.PropertyTaxReference);
        o.HeatingType = d.HeatingType;
        o.EnergySource = d.EnergySource;
        o.RentCapPercent = d.RentCapPercent is 15 ? 15 : 20;
        o.Notes = TrimToNull(d.Notes);
        o.LandlordProfileId = d.LandlordProfileId;
    }

    private static void Apply(Unit e, UnitInput d)
    {
        e.Name = d.Name.Trim();
        e.Location = TrimToNull(d.Location);
        e.Type = d.Type;
        e.RoomCount = d.RoomCount;
        e.Bathroom = TrimToNull(d.Bathroom);
        e.Balcony = d.Balcony;
        e.FittedKitchen = d.FittedKitchen;
        e.EnergyStandard = TrimToNull(d.EnergyStandard);
        e.Amenities = TrimToNull(d.Amenities);
    }

    private static void Apply(Room z, RoomInput d)
    {
        z.Name = d.Name.Trim();
        z.Furnishing = d.Furnishing;
        z.FurnishingList = TrimToNull(d.FurnishingList);
        z.Notes = TrimToNull(d.Notes);
    }

    private static string? TrimToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
