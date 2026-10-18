using System.Security.Claims;

namespace MietHahn.Application.Dateien;

/// <summary>
/// Erweiterungspunkt für den Dateizugriff außerhalb der Verwaltung: Fachmodule (z. B. Dokumente, Tickets)
/// melden, ob eine Datei für den Benutzer freigegeben ist (Mieter sehen nur ihre Dokumente usw.).
/// </summary>
public interface IDateiFreigabe
{
    Task<bool> IstFreigegebenAsync(Guid dateiId, ClaimsPrincipal benutzer, CancellationToken cancellationToken = default);
}
