namespace MietHahn.Application.Audit;

/// <summary>Liest die Änderungshistorie eines Datensatzes aus dem Audit-Log.</summary>
public interface IAenderungsHistorie
{
    /// <param name="entitaetTyp">Klassenname der Entität, z. B. <c>Mietvertrag</c>.</param>
    Task<IReadOnlyList<AuditEintragAnsicht>> LadenAsync(string entitaetTyp, Guid entitaetId, CancellationToken cancellationToken = default);
}

public enum AuditAktion
{
    Angelegt = 0,
    Geaendert = 1,
    Geloescht = 2,
    Gelesen = 3,
}

public sealed record AuditEintragAnsicht(
    DateTimeOffset Zeitpunkt,
    string Benutzer,
    AuditAktion Aktion,
    IReadOnlyList<Feldaenderung> Aenderungen,
    string? Zweck);

/// <param name="Alt">Bisheriger Wert (als Text), <c>null</c> bei Neuanlage.</param>
/// <param name="Neu">Neuer Wert (als Text).</param>
public sealed record Feldaenderung(string Feld, string? Alt, string? Neu);
