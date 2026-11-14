using Elocaro.Domain.Common;
using Elocaro.Domain.Metering;

namespace Elocaro.Infrastructure.Metering;

/// <summary>Consumption of a metering point; a calculated meter (<see cref="Meter.Terms"/>) is the signed sum of its source meters.</summary>
internal static class MeterConsumption
{
    /// <param name="chain">The metering point (chain of replaced meters, or a single calculated meter).</param>
    /// <param name="all">All meters of the property (the source meters are looked up here, with their replacements).</param>
    public static Consumption Calculate(IReadOnlyList<Meter> chain, IReadOnlyList<Meter> all, Period period)
    {
        ArgumentNullException.ThrowIfNull(chain);
        return chain.Count == 1 && chain[0].IsDerived ? Derived(chain[0], all, period) : ConsumptionCalculator.Calculate(chain, period);
    }

    private static Consumption Derived(Meter meter, IReadOnlyList<Meter> all, Period period)
    {
        decimal total = 0;
        var basis = new List<ReadingOnDay>();
        foreach (var term in meter.Terms)
        {
            var source = all.FirstOrDefault(m => m.Id == term.SourceMeterId);
            if (source is null)
            {
                return new Consumption(period, null, basis, "Ein Zähler, aus dem der Rechenzähler berechnet wird, wurde nicht gefunden.");
            }

            var chain = MeterService.MeteringPoint(source, all);
            var consumption = ConsumptionCalculator.Calculate(chain, period);
            if (consumption.Quantity is not { } quantity)
            {
                return new Consumption(period, null, basis, $"Zähler {source.Number}: {consumption.MissingReason}");
            }

            // Result in the unit of the calculated meter (the source meters may count in another unit, e.g. MWh instead of kWh).
            total += term.Sign * quantity * Factor(chain[^1]) / Factor(meter);
            basis.AddRange(consumption.Basis);
        }

        return new Consumption(period, total, basis, null);
    }

    private static decimal Factor(Meter meter) => Elocaro.Infrastructure.OperatingCosts.MeterConsumptionSource.UnitFactor(meter);
}
