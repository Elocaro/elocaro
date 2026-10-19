namespace MietHahn.Domain.Common;

/// <summary>Ein Stand eines zeitabhängigen Werts (Fläche, Mietbestandteil, Personenanzahl …).</summary>
public interface ITimeDependent
{
    Period Validity { get; }

    /// <summary>Beendet den Stand zum angegebenen Tag (inklusive).</summary>
    void EndValidity(DateOnly lastDay);
}

/// <summary>
/// Pflege zeitabhängiger Werte ohne Überschreiben: Ein neuer Stand gilt ab einem Stichtag, der bisherige endet am Vortag.
/// So bleiben frühere Werte für Abrechnungen erhalten (z. B. Fläche vor einem Umbau, Miete vor einer Erhöhung).
/// </summary>
public static class TimeSeries
{
    /// <summary>
    /// Fügt einen neuen, unbefristeten Stand ab <paramref name="from"/> hinzu. Der jüngste bisherige Stand endet am Vortag;
    /// beginnt er genau am Stichtag, wird er ersetzt (Korrektur). Änderungen vor dem jüngsten Stand sind nicht möglich.
    /// </summary>
    /// <returns>Der neue Stand.</returns>
    public static T CarryForward<T>(ICollection<T> readings, DateOnly from, Func<Period, T> create)
        where T : class, ITimeDependent
    {
        ArgumentNullException.ThrowIfNull(readings);
        ArgumentNullException.ThrowIfNull(create);

        var latest = readings.MaxBy(s => s.Validity.From);
        if (latest is not null)
        {
            if (from < latest.Validity.From)
            {
                throw new RuleViolationException(
                    $"Der Stichtag muss am oder nach dem {latest.Validity.From:dd.MM.yyyy} liegen (Beginn des letzten Stands). Frühere Stände bleiben unverändert.");
            }

            if (from == latest.Validity.From)
            {
                readings.Remove(latest);
            }
            else if (latest.Validity.Contains(from))
            {
                latest.EndValidity(from.AddDays(-1));
            }
        }

        var newItem = create(new Period(from));
        readings.Add(newItem);
        return newItem;
    }

    /// <summary>Der am Tag gültige Stand oder <c>null</c>.</summary>
    public static T? At<T>(IEnumerable<T> readings, DateOnly day)
        where T : ITimeDependent =>
        readings.FirstOrDefault(s => s.Validity.Contains(day));
}
