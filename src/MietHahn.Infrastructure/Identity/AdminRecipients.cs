using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MietHahn.Application.Accounts;
using MietHahn.Infrastructure.Persistence;

namespace MietHahn.Infrastructure.Identity;

/// <summary>E-Mail-Adressen aller aktiven (nicht gesperrten) Administratoren, z. B. für Warnungen und Hinweise.</summary>
internal static class AdminRecipients
{
    public static Task<List<string>> GetAsync(MietHahnDbContext db, CancellationToken cancellationToken) =>
        (from user in db.Set<ApplicationUser>()
         join assignment in db.Set<IdentityUserRole<Guid>>() on user.Id equals assignment.UserId
         join role in db.Set<Role>() on assignment.RoleId equals role.Id
         where role.Name == Roles.Admin && !user.Locked && user.Email != null
         select user.Email!)
        .ToListAsync(cancellationToken);
}
