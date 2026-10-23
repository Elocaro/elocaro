using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MietHahn.Application.Accounts;
using MietHahn.Application.Admin;
using MietHahn.Application.Common;
using MietHahn.Application.Metering;
using MietHahn.Application.Todos;
using MietHahn.Domain.Common;
using MietHahn.Domain.Metering;
using MietHahn.Domain.Properties;
using MietHahn.Infrastructure.Common;
using MietHahn.Infrastructure.Persistence;
using ZaehlerEntitaet = MietHahn.Domain.Metering.Meter;

namespace MietHahn.Infrastructure.Metering;

internal sealed class MeterService(MietHahnDbContext db, ICurrentUser user, TimeProvider clock) : IMeterService
{
    private DateOnly Today => GermanTime.Today(clock.GetUtcNow());

    public async Task<IReadOnlyList<MeterRow>> ListAsync(Guid propertyId, bool includeRemoved = false, CancellationToken cancellationToken = default)
    {
        var meter = await WithReadings().Where(z => z.PropertyId == propertyId && (includeRemoved || z.RemovedOn == null)).ToListAsync(cancellationToken);
        var names = await AssignmentNamesAsync(propertyId, cancellationToken);
        return Sorted(meter).Select(z => Row(z, names)).ToList();
    }

    public async Task<MeterDetails?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var meter = await WithReadings().SingleOrDefaultAsync(z => z.Id == id, cancellationToken);
        if (meter is null)
        {
            return null;
        }

        var all = await WithReadings().Where(z => z.PropertyId == meter.PropertyId).ToListAsync(cancellationToken);
        var names = await AssignmentNamesAsync(meter.PropertyId, cancellationToken);
        var property = await db.Set<Property>().AsNoTracking().Where(o => o.Id == meter.PropertyId).Select(o => o.Name).SingleAsync(cancellationToken);
        var meteringPoint = MeteringPoint(meter, all);
        var years = Enumerable.Range(Today.Year - 2, 3).Reverse()
            .Select(j => new AnnualConsumption(j, ConsumptionCalculator.Calculate(meteringPoint, Period.CalendarYear(j))))
            .Where(j => j.Consumption.Quantity is not null || j.Year >= meteringPoint.Min(z => z.InstalledOn).Year)
            .ToList();
        var subMeters = all.Where(z => z.MainMeterId is not null && meteringPoint.Any(m => m.Id == z.MainMeterId)).ToList();

        return new MeterDetails(
            Row(meter, names),
            meter.PropertyId,
            property,
            Data(meter),
            meter.Readings.OrderByDescending(s => s.Date).Select(Entry).ToList(),
            meteringPoint.Select(m => Row(m, names)).ToList(),
            years,
            Sorted(subMeters.Where(u => u.RemovedOn is null)).Select(u => Row(u, names)).ToList(),
            subMeters.Count == 0 ? null : Balance(Today.Year - 1, meteringPoint, subMeters, all));
    }

    public async Task<Result> CreateAsync(Guid propertyId, MeterCreation creation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(creation);
        if (!await db.Set<Property>().AnyAsync(o => o.Id == propertyId, cancellationToken))
        {
            return Command.NotFound;
        }

        return await Command.ExecuteAsync(async () =>
        {
            var data = creation.Data;
            var meter = new ZaehlerEntitaet(
                propertyId,
                data.Type,
                data.Name,
                data.Number,
                creation.InstalledOn ?? throw new RuleViolationException("Bitte das Einbaudatum angeben."),
                creation.InitialReading ?? throw new RuleViolationException("Bitte den Anfangsstand angeben."));
            await ApplyAsync(meter, data, cancellationToken);
            if (data.Order == 0)
            {
                meter.Order = (await db.Set<ZaehlerEntitaet>().Where(z => z.PropertyId == propertyId).MaxAsync(z => (int?)z.Order, cancellationToken) ?? 0) + 1;
            }

            db.Add(meter);
            await db.SaveChangesAsync(cancellationToken);
            return meter.Id;
        });
    }

    public Task<Result> UpdateAsync(Guid id, MeterInput data, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(data);
            var meter = await db.Set<ZaehlerEntitaet>().SingleOrDefaultAsync(z => z.Id == id, cancellationToken) ?? throw new NotFoundException();
            if (meter.Type != data.Type)
            {
                throw new RuleViolationException("Der Zählertyp lässt sich nicht ändern. Bitte einen neuen Zähler anlegen.");
            }

            await ApplyAsync(meter, data, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return id;
        });

    public async Task<ReadingResult> RecordReadingAsync(Guid id, ReadingInput input, Guid? photoFileId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var meter = await WithReadings(tracking: true).SingleOrDefaultAsync(z => z.Id == id, cancellationToken);
        if (meter is null)
        {
            return new ReadingResult(false, ["Der Zähler wurde nicht gefunden."], []);
        }

        if (input.Date is not { } date || input.Value is not { } value)
        {
            return new ReadingResult(false, ["Bitte Datum und Zählerstand angeben."], []);
        }

        var warnings = meter.Validate(date, value);
        if (warnings.Count > 0 && !input.Confirmed)
        {
            return new ReadingResult(false, [], warnings);
        }

        var result = await Command.ExecuteAsync(async () =>
        {
            meter.RecordReading(date, value, input.Kind, user.Identifier, photoFileId, input.Note);
            await db.SaveChangesAsync(cancellationToken);
            return id;
        });
        return new ReadingResult(result.Succeeded, result.Errors, warnings);
    }

    public Task<Result> RemoveReadingAsync(Guid id, Guid readingId, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var meter = await WithReadings(tracking: true).SingleOrDefaultAsync(z => z.Id == id, cancellationToken) ?? throw new NotFoundException();
            meter.RemoveReading(readingId);
            await db.SaveChangesAsync(cancellationToken);
            return id;
        });

    public Task<Result> ReplaceAsync(Guid id, ReplacementInput input, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(input);
            var meter = await WithReadings(tracking: true).SingleOrDefaultAsync(z => z.Id == id, cancellationToken) ?? throw new NotFoundException();
            var newItem = meter.Replace(
                input.Date ?? throw new RuleViolationException("Bitte das Tauschdatum angeben."),
                input.FinalReading ?? throw new RuleViolationException("Bitte den Endstand angeben."),
                input.NewNumber,
                input.InitialReading ?? throw new RuleViolationException("Bitte den Anfangsstand angeben."),
                input.CalibrationDueDate);
            db.Add(newItem);

            // Sub-meters are attached to the new main meter from now on.
            foreach (var subMeters in await db.Set<ZaehlerEntitaet>().Where(z => z.MainMeterId == id && z.RemovedOn == null).ToListAsync(cancellationToken))
            {
                subMeters.MainMeterId = newItem.Id;
            }

            await db.SaveChangesAsync(cancellationToken);
            return newItem.Id;
        });

    public async Task<IReadOnlyList<MeterRow>> RoundAsync(Guid propertyId, Guid? unitId = null, CancellationToken cancellationToken = default)
    {
        var today = Today;
        var meter = await WithReadings()
            .Where(z => z.PropertyId == propertyId && z.RemovedOn == null && z.InstalledOn <= today && (unitId == null || z.UnitId == unitId))
            .ToListAsync(cancellationToken);
        var names = await AssignmentNamesAsync(propertyId, cancellationToken);
        return meter.OrderBy(z => z.Order).ThenBy(z => z.Name, StringComparer.CurrentCulture).Select(z => Row(z, names)).ToList();
    }

    public async Task<Consumption?> ConsumptionAsync(Guid id, Period period, CancellationToken cancellationToken = default)
    {
        var meter = await WithReadings().SingleOrDefaultAsync(z => z.Id == id, cancellationToken);
        if (meter is null)
        {
            return null;
        }

        var all = await WithReadings().Where(z => z.PropertyId == meter.PropertyId && z.Type == meter.Type).ToListAsync(cancellationToken);
        return ConsumptionCalculator.Calculate(MeteringPoint(meter, all), period);
    }

    /// <summary>All meters of the metering point (chain via predecessor/successor), oldest first.</summary>
    internal static List<ZaehlerEntitaet> MeteringPoint(ZaehlerEntitaet meter, IReadOnlyList<ZaehlerEntitaet> all)
    {
        var chain = new List<ZaehlerEntitaet> { meter };
        for (var predecessor = all.FirstOrDefault(z => z.Id == meter.PredecessorId); predecessor is not null; predecessor = all.FirstOrDefault(z => z.Id == predecessor.PredecessorId))
        {
            chain.Insert(0, predecessor);
        }

        for (var successor = all.FirstOrDefault(z => z.PredecessorId == meter.Id); successor is not null; successor = all.FirstOrDefault(z => z.PredecessorId == successor.Id))
        {
            chain.Add(successor);
        }

        return chain;
    }

    private static SubMeterBalance Balance(int year, List<ZaehlerEntitaet> main, List<ZaehlerEntitaet> subMeters, List<ZaehlerEntitaet> all)
    {
        var period = Period.CalendarYear(year);
        var hints = new List<string>();
        var mainConsumption = ConsumptionCalculator.Calculate(main, period);
        if (mainConsumption.MissingReason is not null)
        {
            hints.Add(mainConsumption.MissingReason);
        }

        decimal total = 0;
        var complete = true;
        foreach (var meteringPoint in subMeters.Select(u => MeteringPoint(u, all)).DistinctBy(m => m[0].Id))
        {
            var consumption = ConsumptionCalculator.Calculate(meteringPoint, period);
            if (consumption.Quantity is { } quantity)
            {
                total += quantity;
            }
            else
            {
                complete = false;
                hints.Add(consumption.MissingReason!);
            }
        }

        return new SubMeterBalance(
            year,
            mainConsumption.Quantity,
            complete ? total : null,
            mainConsumption.Quantity is { } h && complete ? h - total : null,
            hints);
    }

    private async Task ApplyAsync(ZaehlerEntitaet meter, MeterInput data, CancellationToken cancellationToken)
    {
        meter.Name = data.Name.Trim();
        meter.Number = data.Number.Trim();
        meter.Location = string.IsNullOrWhiteSpace(data.Location) ? null : data.Location.Trim();
        meter.MeasurementUnit = data.MeasurementUnit ?? meter.MeasurementUnit;
        meter.CalibrationDueDate = data.CalibrationDueDate ?? meter.CalibrationDueDate;
        meter.Notes = string.IsNullOrWhiteSpace(data.Notes) ? null : data.Notes.Trim();
        if (data.Order > 0)
        {
            meter.Order = data.Order;
        }

        // Assignment: unit or room of the same property
        Guid? unitId = null, roomId = null;
        if (data.AssignmentId is { } assignment)
        {
            var unit = await db.Set<Unit>().AsNoTracking().Where(e => e.PropertyId == meter.PropertyId && (e.Id == assignment || e.Room.Any(z => z.Id == assignment)))
                .Select(e => new { e.Id, Room = e.Room.Where(z => z.Id == assignment).Select(z => (Guid?)z.Id).FirstOrDefault() })
                .SingleOrDefaultAsync(cancellationToken) ?? throw new RuleViolationException("Die Zuordnung gehört nicht zu diesem Objekt.");
            unitId = unit.Id;
            roomId = unit.Room;
        }

        meter.Assign(unitId, roomId);

        if (data.MainMeterId is { } mainId)
        {
            var main = await db.Set<ZaehlerEntitaet>().AsNoTracking().SingleOrDefaultAsync(z => z.Id == mainId, cancellationToken);
            if (main is null || main.PropertyId != meter.PropertyId || main.Id == meter.Id)
            {
                throw new RuleViolationException("Der Hauptzähler muss ein anderer Zähler desselben Objekts sein.");
            }

            if (main.Type != meter.Type && !(main.Type == MeterType.ColdWater && meter.Type == MeterType.HotWater))
            {
                throw new RuleViolationException("Haupt- und Unterzähler müssen dasselbe messen (Ausnahme: Warmwasser unter Kaltwasser-Hauptzähler).");
            }
        }

        meter.MainMeterId = data.MainMeterId;
    }

    private IQueryable<ZaehlerEntitaet> WithReadings(bool tracking = false)
    {
        var query = db.Set<ZaehlerEntitaet>().Include(z => z.Readings);
        return tracking ? query : query.AsNoTracking();
    }

    private async Task<Dictionary<Guid, string>> AssignmentNamesAsync(Guid propertyId, CancellationToken cancellationToken)
    {
        var units = await db.Set<Unit>().AsNoTracking().Include(e => e.Room).Where(e => e.PropertyId == propertyId).ToListAsync(cancellationToken);
        var names = units.ToDictionary(e => e.Id, e => e.Name);
        foreach (var room in units.SelectMany(e => e.Room.Select(z => (e, z))))
        {
            names[room.z.Id] = $"{room.e.Name}, {room.z.Name}";
        }

        return names;
    }

    private static IEnumerable<ZaehlerEntitaet> Sorted(IEnumerable<ZaehlerEntitaet> meter) =>
        meter.OrderBy(z => z.Order).ThenBy(z => z.Name, StringComparer.CurrentCulture).ThenBy(z => z.InstalledOn);

    private static MeterRow Row(ZaehlerEntitaet z, IReadOnlyDictionary<Guid, string> names) => new(
        z.Id,
        z.Name,
        z.Type,
        z.Number,
        z.MeasurementUnit,
        z.RoomId is { } room ? names.GetValueOrDefault(room, "?") : z.UnitId is { } unit ? names.GetValueOrDefault(unit, "?") : "Haus",
        z.UnitId,
        z.Location,
        z.Readings.MaxBy(s => s.Date) is { } lastReading ? Entry(lastReading) : null,
        z.CalibrationDueDate,
        z.InstalledOn,
        z.RemovedOn,
        z.MainMeterId,
        z.Order);

    private static ReadingView Entry(MeterReading s) => new(s.Id, s.Date, s.Value, s.Kind, s.ReadBy, s.PhotoFileId, s.Note);

    private static MeterInput Data(ZaehlerEntitaet z) => new()
    {
        Type = z.Type,
        MeasurementUnit = z.MeasurementUnit,
        Name = z.Name,
        Number = z.Number,
        Location = z.Location,
        AssignmentId = z.RoomId ?? z.UnitId,
        MainMeterId = z.MainMeterId,
        CalibrationDueDate = z.CalibrationDueDate,
        Order = z.Order,
        Notes = z.Notes,
    };
}

/// <summary>Dashboard: expired or soon-to-expire calibration periods (90 days).</summary>
[Module(AppModule.Metering)]
internal sealed class CalibrationDueTodos(MietHahnDbContext db, TimeProvider clock) : ITodoSource
{
    public async Task<IReadOnlyList<TodoItem>> TodosAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!user.IsInRole(Roles.Admin) && !user.HasClaim(Permissions.ClaimType, nameof(AccessRight.MasterData)))
        {
            return [];
        }

        var today = GermanTime.Today(clock.GetUtcNow());
        var cutoff = today.AddDays(90);
        var due = await db.Set<ZaehlerEntitaet>().AsNoTracking()
            .Where(z => z.RemovedOn == null && z.CalibrationDueDate != null && z.CalibrationDueDate <= cutoff)
            .OrderBy(z => z.CalibrationDueDate)
            .Select(z => new { z.Id, z.Name, z.Number, z.CalibrationDueDate })
            .ToListAsync(cancellationToken);
        return due.Select(z => new TodoItem(
            "Zähler",
            z.CalibrationDueDate < today ? $"Eichfrist abgelaufen: {z.Name} ({z.Number})" : $"Eichfrist läuft ab: {z.Name} ({z.Number})",
            $"/verwaltung/zaehler/{z.Id}",
            z.CalibrationDueDate < today ? Urgency.Urgent : Urgency.Soon,
            "Nicht geeichte Zähler dürfen nicht für die Abrechnung verwendet werden: Zähler tauschen lassen.",
            z.CalibrationDueDate)).ToList();
    }
}
