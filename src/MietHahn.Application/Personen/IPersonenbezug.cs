namespace MietHahn.Application.Personen;

/// <summary>
/// Erweiterungspunkt für Fachmodule: in welcher Rolle eine Person vorkommt (Mieter, Bürge, Ticket …). Bestimmt die
/// Rollenanzeige, verhindert das Löschen und liefert das Ende der letzten Beziehung für die Aufbewahrungsfrist.
/// </summary>
public interface IPersonenbezug
{
    Task<IReadOnlyList<Personenbezug>> BezuegeAsync(IReadOnlyCollection<Guid> personIds, CancellationToken cancellationToken = default);
}

/// <param name="Rolle">z. B. „Mieter“, „Bürge“.</param>
/// <param name="Beschreibung">z. B. „Wohnung 1, Lindenweg 3 (seit 01.03.2026)“.</param>
/// <param name="Aktiv">Läuft die Beziehung noch (z. B. Vertrag nicht beendet)?</param>
/// <param name="EndeAm">Ende der Beziehung; Grundlage der Aufbewahrungsfrist.</param>
public sealed record Personenbezug(Guid PersonId, string Rolle, string Beschreibung, bool Aktiv, DateOnly? EndeAm, string? Link = null);

/// <summary>Erweiterungspunkt für die DSGVO-Auskunft: jedes Modul liefert die zur Person gespeicherten Daten.</summary>
public interface IPersonendatenQuelle
{
    /// <summary>Abschnittsname in der Auskunft, z. B. „mietvertraege“.</summary>
    string Abschnitt { get; }

    /// <returns>Serialisierbare Daten oder <c>null</c>, wenn das Modul nichts zur Person speichert.</returns>
    Task<object?> DatenAsync(Guid personId, CancellationToken cancellationToken = default);
}
