using MietHahn.Domain.Common;
using MietHahn.Domain.Metering;
using MietHahn.Domain.OperatingCosts;
using MietHahn.Infrastructure.Metering;
using Zaehler = MietHahn.Domain.Metering.Meter;

namespace MietHahn.Infrastructure.OperatingCosts;

/// <summary>
/// Consumption of the meters assigned to a unit (not to a room): metering points (chains of replaced meters) are summed up,
/// readings are interpolated at the reference dates (e.g. on tenant change), see <see cref="ConsumptionCalculator"/>.
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

        decimal total = 0;
        var interpolated = false;
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

            total += quantity;
            interpolated |= consumption.ContainsInterpolation;
            notes.Add($"{string.Join(" → ", chain.Select(m => m.Number))}: {quantity:0.###}");
        }

        return new ConsumptionQuantity(total, interpolated, string.Join("; ", notes));
    }

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
