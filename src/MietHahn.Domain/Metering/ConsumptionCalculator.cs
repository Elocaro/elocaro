using MietHahn.Domain.Common;

namespace MietHahn.Domain.Metering;

/// <summary>A meter reading on a day; <see cref="Interpolated"/> marks calculated values.</summary>
public sealed record ReadingOnDay(DateOnly Date, decimal Value, bool Interpolated);

/// <summary>Consumption in a period with proof of the readings used.</summary>
/// <param name="Quantity">Consumption; <c>null</c> if readings are missing (see <see cref="MissingReason"/>).</param>
public sealed record Consumption(Period Period, decimal? Quantity, IReadOnlyList<ReadingOnDay> Basis, string? MissingReason)
{
    public bool ContainsInterpolation => Basis.Any(g => g.Interpolated);
}

/// <summary>
/// Calculation core for consumption: reading on a reference date (measured or linearly interpolated between two readings)
/// and consumption over a measuring point, including across a meter replacement. Extrapolation beyond the last reading
/// is not done; the value is then missing and must be read or estimated.
/// </summary>
public static class ConsumptionCalculator
{
    /// <summary>Reading on the day: measured, otherwise linearly interpolated; <c>null</c> outside the readings.</summary>
    public static ReadingOnDay? ReadingAt(IEnumerable<MeterReading> readings, DateOnly day)
    {
        ArgumentNullException.ThrowIfNull(readings);
        var list = readings.OrderBy(s => s.Date).ToList();
        var exact = list.FirstOrDefault(s => s.Date == day);
        if (exact is not null)
        {
            return new ReadingOnDay(day, exact.Value, false);
        }

        var before = list.LastOrDefault(s => s.Date < day);
        var after = list.FirstOrDefault(s => s.Date > day);
        if (before is null || after is null)
        {
            return null;
        }

        var share = (decimal)(day.DayNumber - before.Date.DayNumber) / (after.Date.DayNumber - before.Date.DayNumber);
        return new ReadingOnDay(day, Math.Round(before.Value + ((after.Value - before.Value) * share), 3, MidpointRounding.AwayFromZero), true);
    }

    /// <summary>
    /// Consumption of a measuring point (chain of replaced meters, oldest first) from the reading at the end of the day before
    /// <c>zeitraum.Von</c> to the reading at the end of <c>zeitraum.Bis</c>. Readings count as the reading at the end of the day.
    /// </summary>
    public static Consumption Calculate(IReadOnlyList<Meter> meteringPoint, Period period)
    {
        ArgumentNullException.ThrowIfNull(meteringPoint);
        if (period.To is not { } to)
        {
            throw new ArgumentException("Der Zeitraum muss befristet sein.", nameof(period));
        }

        var start = period.From.AddDays(-1);
        var basis = new List<ReadingOnDay>();
        decimal total = 0;
        foreach (var meter in meteringPoint.OrderBy(z => z.InstalledOn))
        {
            // Section of this meter within the period
            var from = start > meter.InstalledOn ? start : meter.InstalledOn;
            var end = meter.RemovedOn is { } removal && removal < to ? removal : to;
            if (end < from)
            {
                continue;
            }

            var opening = ReadingAt(meter.Readings, from);
            var closing = ReadingAt(meter.Readings, end);
            if (opening is null || closing is null)
            {
                var day = opening is null ? from : end;
                return new Consumption(period, null, basis, $"Für Zähler {meter.Number} fehlt ein Stand um den {day:dd.MM.yyyy} (Ablesung oder Schätzung erfassen).");
            }

            basis.Add(opening);
            basis.Add(closing);
            total += closing.Value - opening.Value;
        }

        return basis.Count == 0
            ? new Consumption(period, null, basis, "Im Zeitraum war kein Zähler eingebaut.")
            : new Consumption(period, total, basis, null);
    }
}
