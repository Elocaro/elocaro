using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MietHahn.Application.Aufgaben;
using MietHahn.Application.Konten;
using MietHahn.Domain.Kommunikation;
using MietHahn.Infrastructure.Persistenz;

namespace MietHahn.Infrastructure.Mail;

/// <summary>Meldet endgültig fehlgeschlagene E-Mails im Dashboard (für Konten mit Systemrecht).</summary>
internal sealed class MailAufgaben(MietHahnDbContext db) : IAufgabenQuelle
{
    public async Task<IReadOnlyList<Aufgabe>> AufgabenAsync(ClaimsPrincipal benutzer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(benutzer);
        if (!benutzer.IsInRole(Rollen.Admin) && !benutzer.HasClaim(Rechte.ClaimTyp, nameof(Recht.System)))
        {
            return [];
        }

        var anzahl = await db.Set<AusgehendeMail>().CountAsync(m => m.Status == MailStatus.Fehlgeschlagen, cancellationToken);
        return anzahl == 0
            ? []
            : [new Aufgabe("System", anzahl == 1 ? "1 E-Mail konnte nicht zugestellt werden" : $"{anzahl} E-Mails konnten nicht zugestellt werden",
                "/verwaltung/system/mails", Dringlichkeit.Dringend, "Zugangsdaten des Mailservers prüfen und erneut senden.")];
    }
}
