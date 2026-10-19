using MietHahn.Domain.Common;

namespace MietHahn.Domain.Metering;

/// <summary>Ein Zählerstand zu einem Tag; <see cref="Interpolated"/> kennzeichnet berechnete Werte.</summary>
public sealed record ReadingOnDay(DateOnly Date, decimal Value, bool Interpolated);

/// <summary>Verbrauch in einem Zeitraum mit Nachweis der verwendeten Stände.</summary>
/// <param name="Quantity">Verbrauch; <c>null</c>, wenn Stände fehlen (siehe <see cref="MissingReason"/>).</param>
public sealed record Consumption(Period Period, decimal? Quantity, IReadOnlyList<ReadingOnDay> Basis, string? MissingReason)
{
    public bool ContainsInterpolation => Basis.Any(g => g.Interpolated);
}

/// <summary>
/// Rechenkern für Verbräuche: Stand an einem Stichtag (gemessen oder linear zwischen zwei Ablesungen interpoliert)
/// und Verbrauch über eine Messstelle, auch über einen Zählertausch hinweg. Hochrechnen über die letzte Ablesung hinaus
/// erfolgt nicht; dann fehlt der Wert und muss abgelesen oder geschätzt werden.
/// </summary>
public static class ConsumptionCalculator
{
    /// <summary>Stand am Tag: gemessen, sonst linear interpoliert; <c>null</c> außerhalb der Ablesungen.</summary>
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
    /// Verbrauch einer Messstelle (Kette getauschter Zähler, älteste zuerst) vom Stand am Ende des Vortags von
    /// <c>zeitraum.Von</c> bis zum Stand am Ende von <c>zeitraum.Bis</c>. Ablesungen gelten als Stand am Ende des Tages.
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
            // Abschnitt dieses Zählers innerhalb des Zeitraums
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
