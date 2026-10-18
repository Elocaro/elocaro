using Microsoft.EntityFrameworkCore;
using MietHahn.Application.Audit;
using MietHahn.Infrastructure.Persistenz;

namespace MietHahn.Infrastructure.Audit;

internal sealed class AenderungsHistorie(MietHahnDbContext db) : IAenderungsHistorie
{
    public async Task<IReadOnlyList<AuditEintragAnsicht>> LadenAsync(string entitaetTyp, Guid entitaetId, CancellationToken cancellationToken = default)
    {
        var eintraege = await db.Set<AuditEintrag>()
            .AsNoTracking()
            .Where(a => a.EntitaetTyp == entitaetTyp && a.EntitaetId == entitaetId)
            .OrderByDescending(a => a.Zeitpunkt)
            .ThenByDescending(a => a.Id)
            .ToListAsync(cancellationToken);

        return eintraege
            .Select(a => new AuditEintragAnsicht(a.Zeitpunkt, a.Benutzer, a.Aktion, AuditErfassung.Lesen(a.AenderungenJson), a.Zweck))
            .ToList();
    }
}
