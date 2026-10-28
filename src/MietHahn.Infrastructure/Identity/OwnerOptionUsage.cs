using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MietHahn.Application.Accounts;
using MietHahn.Application.Admin;
using MietHahn.Infrastructure.Persistence;

namespace MietHahn.Infrastructure.Identity;

/// <summary>How many owners have an account (the owner portal).</summary>
internal sealed class OwnerOptionUsage(MietHahnDbContext db) : IOptionUsageSource
{
    public async Task<IReadOnlyDictionary<AppOption, int>> CountAsync(CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<AppOption, int>();
        var owners = await db.Set<IdentityUserRole<Guid>>()
            .Join(db.Set<Role>(), ur => ur.RoleId, r => r.Id, (ur, r) => r.Name)
            .CountAsync(name => name == Roles.Owner, cancellationToken);
        if (owners > 0)
        {
            result[AppOption.TaxOwnerPortal] = owners;
        }

        return result;
    }
}
