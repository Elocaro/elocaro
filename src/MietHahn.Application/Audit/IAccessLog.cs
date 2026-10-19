namespace MietHahn.Application.Audit;

/// <summary>
/// Protokolliert lesende Zugriffe auf personenbezogene Daten (DSGVO-Nachweis), z. B. beim Öffnen einer Mieterakte.
/// Abschaltbar über <c>Audit:ZugriffeProtokollieren</c>.
/// </summary>
public interface IAccessLog
{
    Task LogAsync(string entityType, Guid entityId, string purpose, CancellationToken cancellationToken = default);
}
