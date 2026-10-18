using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MietHahn.Application.Konten;
using MietHahn.Infrastructure.Persistenz;

namespace MietHahn.Infrastructure.Identitaet;

/// <summary>E-Mail-Adressen aller aktiven (nicht gesperrten) Administratoren, z. B. für Warnungen und Hinweise.</summary>
internal static class AdminEmpfaenger
{
    public static Task<List<string>> LadenAsync(MietHahnDbContext db, CancellationToken cancellationToken) =>
        (from benutzer in db.Set<Benutzer>()
         join zuordnung in db.Set<IdentityUserRole<Guid>>() on benutzer.Id equals zuordnung.UserId
         join rolle in db.Set<Rolle>() on zuordnung.RoleId equals rolle.Id
         where rolle.Name == Rollen.Admin && !benutzer.Gesperrt && benutzer.Email != null
         select benutzer.Email!)
        .ToListAsync(cancellationToken);
}
