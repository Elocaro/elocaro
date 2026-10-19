namespace MietHahn.Application.People;

/// <summary>
/// Erweiterungspunkt für Fachmodule: in welcher Rolle eine Person vorkommt (Mieter, Bürge, Ticket …). Bestimmt die
/// Rollenanzeige, verhindert das Löschen und liefert das Ende der letzten Beziehung für die Aufbewahrungsfrist.
/// </summary>
public interface IPersonReference
{
    Task<IReadOnlyList<PersonRelation>> ReferencesAsync(IReadOnlyCollection<Guid> personIds, CancellationToken cancellationToken = default);
}

/// <param name="Role">z. B. „Mieter“, „Bürge“.</param>
/// <param name="Description">z. B. „Wohnung 1, Lindenweg 3 (seit 01.03.2026)“.</param>
/// <param name="Active">Läuft die Beziehung noch (z. B. Vertrag nicht beendet)?</param>
/// <param name="EndsOn">Ende der Beziehung; Grundlage der Aufbewahrungsfrist.</param>
public sealed record PersonRelation(Guid PersonId, string Role, string Description, bool Active, DateOnly? EndsOn, string? Link = null);

/// <summary>Erweiterungspunkt für die DSGVO-Auskunft: jedes Modul liefert die zur Person gespeicherten Daten.</summary>
public interface IPersonalDataSource
{
    /// <summary>Abschnittsname in der Auskunft, z. B. „mietvertraege“.</summary>
    string Section { get; }

    /// <returns>Serialisierbare Daten oder <c>null</c>, wenn das Modul nichts zur Person speichert.</returns>
    Task<object?> DataAsync(Guid personId, CancellationToken cancellationToken = default);
}
