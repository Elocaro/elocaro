using Elocaro.Application.Common;
using Elocaro.Application.Heating;
using Elocaro.Domain.Common;
using Elocaro.Domain.Heating;
using Elocaro.Domain.Metering;
using Elocaro.Domain.Properties;
using Elocaro.Infrastructure.Common;
using Elocaro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Elocaro.Infrastructure.Heating;

internal sealed class HeatingSettingsService(ElocaroDbContext db) : IHeatingSettings
{
    public async Task<HeatingSettingsView?> GetAsync(Guid propertyId, CancellationToken cancellationToken = default)
    {
        var property = await db.Set<Property>().AsNoTracking().Where(o => o.Id == propertyId).Select(o => new { o.Id, o.Name, o.EnergySource }).SingleOrDefaultAsync(cancellationToken);
        if (property is null)
        {
            return null;
        }

        var settings = await db.Set<HeatingSettings>().AsNoTracking().SingleOrDefaultAsync(h => h.PropertyId == propertyId, cancellationToken);
        var meters = await db.Set<Meter>().AsNoTracking()
            .Where(m => m.PropertyId == propertyId && m.RemovedOn == null && (m.Type == MeterType.Heat || m.Type == MeterType.OilTank))
            .OrderBy(m => m.Name).ThenBy(m => m.Number)
            .ToListAsync(cancellationToken);
        var input = settings is null ? Defaults(property.EnergySource) : ToInput(settings);
        var heatMeters = meters.Where(m => m.Type == MeterType.Heat && m.UnitId is null).Select(Option).ToList();
        var oilMeters = meters.Where(m => m.Type == MeterType.OilTank).Select(Option).ToList();
        await AddSelectedAsync(heatMeters, input.HotWaterHeatMeterId, cancellationToken);
        await AddSelectedAsync(oilMeters, input.OilTankMeterId, cancellationToken);

        var carbon = CarbonCostTables.All.OrderByDescending(t => t.ValidFromYear).First();
        var stations = (await db.Set<DegreeDayValue>().AsNoTracking().GroupBy(v => v.StationId).Select(g => new { Id = g.Key, Name = g.OrderByDescending(v => v.Month).First().StationName }).ToListAsync(cancellationToken))
            .OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).Select(s => new DegreeDayStationOption(s.Id, s.Name)).ToList();
        var degreeDays = DegreeDayTables.All.OrderByDescending(t => t.ValidFromYear).First();
        var version = degreeDays.Version;
        var status = degreeDays.Status;
        if (input.DegreeDayStationId is { } stationId)
        {
            var values = await db.Set<DegreeDayValue>().AsNoTracking().Where(v => v.StationId == stationId).ToListAsync(cancellationToken);
            if (DegreeDayStationTable.Build(values, Period.CalendarYear(DateTime.UtcNow.Year)) is { } table)
            {
                (version, status) = (table.Version, table.Status);
            }
        }

        return new HeatingSettingsView(property.Id, property.Name, settings is not null, input, heatMeters, oilMeters, carbon.Version, carbon.Status, version, status, stations);
    }

    public Task<Result> SaveAsync(Guid propertyId, HeatingSettingsInput input, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(input);
            if (!await db.Set<Property>().AnyAsync(o => o.Id == propertyId, cancellationToken))
            {
                throw new NotFoundException();
            }

            await CheckMeterAsync(propertyId, input.HotWaterHeatMeterId, MeterType.Heat, "Der Wärmezähler im Warmwasserkreis", cancellationToken);
            await CheckMeterAsync(propertyId, input.OilTankMeterId, MeterType.OilTank, "Der Tankzähler", cancellationToken);

            var settings = await db.Set<HeatingSettings>().SingleOrDefaultAsync(h => h.PropertyId == propertyId, cancellationToken);
            if (settings is null)
            {
                settings = new HeatingSettings(propertyId);
                db.Add(settings);
            }

            settings.Fuel = input.Fuel;
            settings.ConsumptionPercent = input.ConsumptionPercent ?? throw new RuleViolationException("Bitte den verbrauchsabhängigen Anteil angeben.");
            settings.JointPlant = input.JointPlant;
            settings.HotWaterHeatMeterId = input.HotWaterHeatMeterId;
            settings.SplitBasis = input.SplitBasis;
            settings.HotWaterFormulaFactor = input.HotWaterFormulaFactor ?? 2.5m;
            settings.HotWaterTemperature = input.HotWaterTemperature ?? 60m;
            settings.FuelKwhPerUnit = input.FuelKwhPerUnit;
            settings.CarbonKgPerKwh = input.CarbonKgPerKwh;
            settings.EnergyYears = [.. input.EnergyYears.Where(e => e.CalorificValue is not null || e.StateNumber is not null)
                .Select(e => new FuelEnergyYear(e.Year, e.CalorificValue ?? throw new RuleViolationException("Bitte den Brennwert angeben."), e.StateNumber ?? throw new RuleViolationException("Bitte die Zustandszahl angeben."), string.IsNullOrWhiteSpace(e.Note) ? null : e.Note.Trim(), e.CarbonPricePerTon)).OrderBy(e => e.Year)];
            if (!string.IsNullOrEmpty(input.DegreeDayStationId) && !await db.Set<DegreeDayValue>().AnyAsync(v => v.StationId == input.DegreeDayStationId, cancellationToken))
            {
                throw new RuleViolationException("Die Wetterstation ist unbekannt.");
            }

            settings.DegreeDayStationId = string.IsNullOrEmpty(input.DegreeDayStationId) ? null : input.DegreeDayStationId;
            settings.OilTankMeterId = input.OilTankMeterId;
            settings.OilValuation = input.OilValuation;
            settings.OilOpeningPricePerLiter = input.OilOpeningPricePerLiter;
            settings.ApplyCarbonCosts = input.ApplyCarbonCosts;
            settings.CarbonSpecialCase = input.CarbonSpecialCase;
            settings.CarbonNote = string.IsNullOrWhiteSpace(input.CarbonNote) ? null : input.CarbonNote.Trim();
            settings.Validate();
            await db.SaveChangesAsync(cancellationToken);
            return settings.Id;
        });

    private static HeatingSettingsInput Defaults(EnergySource source) => new()
    {
        JointPlant = true,
        ApplyCarbonCosts = true,
        Fuel = source == EnergySource.HeatingOil ? HeatingFuel.Oil : source is EnergySource.NaturalGas or EnergySource.LiquefiedGas or EnergySource.Unknown ? HeatingFuel.Gas : HeatingFuel.Other,
    };

    private static HeatingSettingsInput ToInput(HeatingSettings s) => new()
    {
        Fuel = s.Fuel,
        ConsumptionPercent = s.ConsumptionPercent,
        JointPlant = s.JointPlant,
        HotWaterHeatMeterId = s.HotWaterHeatMeterId,
        SplitBasis = s.SplitBasis,
        HotWaterFormulaFactor = s.HotWaterFormulaFactor,
        HotWaterTemperature = s.HotWaterTemperature,
        FuelKwhPerUnit = s.FuelKwhPerUnit,
        CarbonKgPerKwh = s.CarbonKgPerKwh,
        EnergyYears = [.. s.EnergyYears.Select(e => new FuelEnergyYearInput { Year = e.Year, CalorificValue = e.CalorificValue, StateNumber = e.StateNumber, Note = e.Note, CarbonPricePerTon = e.CarbonPricePerTon })],
        OilTankMeterId = s.OilTankMeterId,
        DegreeDayStationId = s.DegreeDayStationId,
        OilValuation = s.OilValuation,
        OilOpeningPricePerLiter = s.OilOpeningPricePerLiter,
        ApplyCarbonCosts = s.ApplyCarbonCosts,
        CarbonSpecialCase = s.CarbonSpecialCase,
        CarbonNote = s.CarbonNote,
    };

    private static MeterOption Option(Meter meter) => new(meter.Id, $"{meter.Name} ({meter.Number})");

    private async Task AddSelectedAsync(List<MeterOption> options, Guid? selected, CancellationToken cancellationToken)
    {
        if (selected is { } id && options.All(o => o.Id != id) && await db.Set<Meter>().AsNoTracking().SingleOrDefaultAsync(m => m.Id == id, cancellationToken) is { } meter)
        {
            options.Add(Option(meter));
        }
    }

    private async Task CheckMeterAsync(Guid propertyId, Guid? meterId, MeterType type, string label, CancellationToken cancellationToken)
    {
        if (meterId is { } id && !await db.Set<Meter>().AnyAsync(m => m.Id == id && m.PropertyId == propertyId && m.Type == type, cancellationToken))
        {
            throw new RuleViolationException($"{label} gehört nicht zu diesem Objekt oder hat den falschen Typ.");
        }
    }
}
