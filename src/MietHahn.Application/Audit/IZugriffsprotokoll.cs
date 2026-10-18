namespace MietHahn.Application.Audit;

/// <summary>
/// Protokolliert lesende Zugriffe auf personenbezogene Daten (DSGVO-Nachweis), z. B. beim Öffnen einer Mieterakte.
/// Abschaltbar über <c>Audit:ZugriffeProtokollieren</c>.
/// </summary>
public interface IZugriffsprotokoll
{
    Task ProtokollierenAsync(string entitaetTyp, Guid entitaetId, string zweck, CancellationToken cancellationToken = default);
}
