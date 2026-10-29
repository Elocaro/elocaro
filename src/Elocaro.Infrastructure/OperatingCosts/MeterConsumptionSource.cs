using Elocaro.Domain.Common;
using Elocaro.Domain.Metering;
using Elocaro.Domain.OperatingCosts;
using Elocaro.Infrastructure.Metering;
using Zaehler = Elocaro.Domain.Metering.Meter;

namespace Elocaro.Infrastructure.OperatingCosts;

/// <summary>
/// Consumption of the meters assigned to a unit (not to a room): metering points (chains of replaced meters) are summed up,
/// readings are interpolated at the reference dates (e.g. on tenant change), see <see cref="ConsumptionCalculator"/>.
/// Heat quantities are returned in kWh and water in m³, whatever unit the meter counts in.
/// </summary>
internal sealed class MeterConsumptionSource(IReadOnlyList<Zaehler> meters) : IConsumptionSource
{
    public ConsumptionQuantity Quantity(Guid unitId, MeterType type, Period period)
    {
        var ofType = meters.Where(m => m.Type == type).ToList();
        var assigned = ofType.Where(m => m.UnitId == unitId && m.RoomId is null).ToList();
        if (assigned.Count == 0)
        {
            return new ConsumptionQuantity(null, false, $"Der Einheit ist kein Zähler vom Typ „{Label(type)}“ zugeordnet.");
        }

        return Sum(assigned, ofType, period);
    }

    /// <summary>Consumption of one specific metering point (e.g. the heat meter in the hot water circuit), also across replacements.</summary>
    public ConsumptionQuantity MeterQuantity(Guid meterId, Period period)
    {
        var meter = meters.FirstOrDefault(m => m.Id == meterId);
        if (meter is null)
        {
            return new ConsumptionQuantity(null, false, "Der Zähler wurde nicht gefunden.");
        }

        return Sum([meter], [.. meters.Where(m => m.Type == meter.Type)], period);
    }

    /// <summary>Consumption of the chosen meters (each metering point once, also across replacements).</summary>
    public ConsumptionQuantity MetersQuantity(IReadOnlyCollection<Guid> meterIds, Period period)
    {
        var chosen = meters.Where(m => meterIds.Contains(m.Id)).ToList();
        if (chosen.Count == 0)
        {
            return new ConsumptionQuantity(null, false, "Die gewählten Zähler wurden nicht gefunden.");
        }

        return Sum(chosen, [.. meters.Where(m => m.Type == chosen[0].Type)], period);
    }

    /// <summary>Consumption of all meters of the type that are assigned to a unit or a room (e.g. the flat electricity meters).</summary>
    public ConsumptionQuantity AssignedQuantity(MeterType type, Period period)
    {
        var ofType = meters.Where(m => m.Type == type).ToList();
        var assigned = ofType.Where(m => m.UnitId is not null || m.RoomId is not null).ToList();
        return assigned.Count == 0 ? new ConsumptionQuantity(0m) : Sum(assigned, ofType, period);
    }

    /// <summary>Unit the meters of the type count in, for fuel quantities (m³ or kWh).</summary>
    public string? FuelUnit(IReadOnlyCollection<Guid> meterIds) =>
        meters.FirstOrDefault(m => meterIds.Contains(m.Id))?.MeasurementUnit switch
        {
            MeasurementUnit.CubicMeters => "m³",
            MeasurementUnit.KilowattHours => "kWh",
            _ => null,
        };

    private static ConsumptionQuantity Sum(List<Zaehler> assigned, List<Zaehler> ofType, Period period)
    {
        decimal total = 0;
        var interpolated = new List<DateOnly>();
        var notes = new List<string>();
        var seen = new HashSet<Guid>();
        foreach (var meter in assigned.OrderBy(m => m.InstalledOn).ThenBy(m => m.Id))
        {
            var chain = MeterService.MeteringPoint(meter, ofType);
            if (!seen.Add(chain[0].Id))
            {
                continue;
            }

            var consumption = MeterConsumption.Calculate(chain, ofType, period);
            if (consumption.Quantity is not { } quantity)
            {
                return new ConsumptionQuantity(null, false, consumption.MissingReason);
            }

            var factor = UnitFactor(chain[^1]);
            total += quantity * factor;
            interpolated.AddRange(consumption.Basis.Where(g => g.Interpolated).Select(g => g.Date));
            notes.Add($"{string.Join(" → ", chain.Select(m => m.Number))}: {quantity * factor:0.###}");
        }

        return new ConsumptionQuantity(total, interpolated.Count > 0, string.Join("; ", notes), [.. interpolated.Distinct()]);
    }

    /// <summary>Heat in kWh (meters in MWh are converted), water in m³ (liters are converted).</summary>
    internal static decimal UnitFactor(Zaehler meter) => (meter.Type, meter.MeasurementUnit) switch
    {
        (MeterType.Heat, MeasurementUnit.MegawattHours) => 1000m,
        (MeterType.ColdWater or MeterType.HotWater, MeasurementUnit.Liters) => 0.001m,
        _ => 1m,
    };

    private static string Label(MeterType type) => type switch
    {
        MeterType.ColdWater => "Kaltwasser",
        MeterType.HotWater => "Warmwasser",
        MeterType.Heat => "Wärme",
        MeterType.Electricity => "Strom",
        MeterType.Gas => "Gas",
        _ => "Öl",
    };
}
