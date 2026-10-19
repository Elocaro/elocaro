using System.Security.Claims;

namespace MietHahn.Application.Files;

/// <summary>
/// Erweiterungspunkt für den Dateizugriff außerhalb der Verwaltung: Fachmodule (z. B. Dokumente, Tickets)
/// melden, ob eine Datei für den Benutzer freigegeben ist (Mieter sehen nur ihre Dokumente usw.).
/// </summary>
public interface IFileShare
{
    Task<bool> IsSharedAsync(Guid fileId, ClaimsPrincipal user, CancellationToken cancellationToken = default);
}
