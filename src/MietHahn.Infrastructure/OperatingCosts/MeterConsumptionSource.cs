using MietHahn.Domain.Common;
using MietHahn.Domain.Metering;
using MietHahn.Domain.OperatingCosts;
using MietHahn.Infrastructure.Metering;
using Zaehler = MietHahn.Domain.Metering.Meter;

namespace MietHahn.Infrastructure.OperatingCosts;

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

            var consumption = ConsumptionCalculator.Calculate(chain, period);
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
    private static decimal UnitFactor(Zaehler meter) => (meter.Type, meter.MeasurementUnit) switch
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
