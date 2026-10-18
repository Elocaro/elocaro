namespace MietHahn.Domain.Gemeinsam;

/// <summary>Ein Stand eines zeitabhängigen Werts (Fläche, Mietbestandteil, Personenanzahl …).</summary>
public interface IZeitabhaengig
{
    Zeitraum Gueltigkeit { get; }

    /// <summary>Beendet den Stand zum angegebenen Tag (inklusive).</summary>
    void GueltigkeitBeenden(DateOnly letzterTag);
}

/// <summary>
/// Pflege zeitabhängiger Werte ohne Überschreiben: Ein neuer Stand gilt ab einem Stichtag, der bisherige endet am Vortag.
/// So bleiben frühere Werte für Abrechnungen erhalten (z. B. Fläche vor einem Umbau, Miete vor einer Erhöhung).
/// </summary>
public static class Zeitreihe
{
    /// <summary>
    /// Fügt einen neuen, unbefristeten Stand ab <paramref name="ab"/> hinzu. Der jüngste bisherige Stand endet am Vortag;
    /// beginnt er genau am Stichtag, wird er ersetzt (Korrektur). Änderungen vor dem jüngsten Stand sind nicht möglich.
    /// </summary>
    /// <returns>Der neue Stand.</returns>
    public static T Fortschreiben<T>(ICollection<T> staende, DateOnly ab, Func<Zeitraum, T> erzeugen)
        where T : class, IZeitabhaengig
    {
        ArgumentNullException.ThrowIfNull(staende);
        ArgumentNullException.ThrowIfNull(erzeugen);

        var juengster = staende.MaxBy(s => s.Gueltigkeit.Von);
        if (juengster is not null)
        {
            if (ab < juengster.Gueltigkeit.Von)
            {
                throw new RegelverletzungException(
                    $"Der Stichtag muss am oder nach dem {juengster.Gueltigkeit.Von:dd.MM.yyyy} liegen (Beginn des letzten Stands). Frühere Stände bleiben unverändert.");
            }

            if (ab == juengster.Gueltigkeit.Von)
            {
                staende.Remove(juengster);
            }
            else if (juengster.Gueltigkeit.Enthaelt(ab))
            {
                juengster.GueltigkeitBeenden(ab.AddDays(-1));
            }
        }

        var neu = erzeugen(new Zeitraum(ab));
        staende.Add(neu);
        return neu;
    }

    /// <summary>Der am Tag gültige Stand oder <c>null</c>.</summary>
    public static T? Am<T>(IEnumerable<T> staende, DateOnly tag)
        where T : IZeitabhaengig =>
        staende.FirstOrDefault(s => s.Gueltigkeit.Enthaelt(tag));
}
