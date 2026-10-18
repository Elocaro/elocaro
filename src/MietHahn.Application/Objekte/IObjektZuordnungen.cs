using MietHahn.Application.Gemeinsam;

namespace MietHahn.Application.Objekte;

/// <summary>Welche Objekte ein Hausmeister betreut (sieht er im Servicebereich).</summary>
public interface IObjektZuordnungen
{
    Task<IReadOnlyList<Guid>> ObjekteVonAsync(Guid benutzerId, CancellationToken cancellationToken = default);

    Task<bool> IstZugeordnetAsync(Guid benutzerId, Guid objektId, CancellationToken cancellationToken = default);

    /// <summary>Ersetzt die Zuordnungen des Kontos; nur für Hausmeister.</summary>
    Task<Ergebnis> SetzenAsync(Guid benutzerId, IReadOnlyCollection<Guid> objektIds, CancellationToken cancellationToken = default);
}
