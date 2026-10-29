using Elocaro.Application.Accounts;
using Elocaro.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Elocaro.Infrastructure.Identity;

/// <summary>Email addresses of all active (not blocked) administrators, e.g. for warnings and notices.</summary>
internal static class AdminRecipients
{
    public static Task<List<string>> GetAsync(ElocaroDbContext db, CancellationToken cancellationToken) =>
        (from user in db.Set<ApplicationUser>()
         join assignment in db.Set<IdentityUserRole<Guid>>() on user.Id equals assignment.UserId
         join role in db.Set<Role>() on assignment.RoleId equals role.Id
         where role.Name == Roles.Admin && !user.Locked && user.Email != null
         select user.Email!)
        .ToListAsync(cancellationToken);
}
