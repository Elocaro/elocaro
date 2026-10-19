using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MietHahn.Application.Accounts;
using MietHahn.Application.Audit;
using MietHahn.Infrastructure.Audit;
using MietHahn.Infrastructure.Persistence;

namespace MietHahn.Infrastructure.Identity;

/// <remarks>
/// Role and permission changes take effect on the next request because the security stamp validator rebuilds the principal
/// from the database on every request (interval 0). Only blocking renews the stamp and thereby signs the user out.
/// </remarks>
internal sealed class UserManagement(MietHahnDbContext db, UserManager<ApplicationUser> userManager, AuditWriter audit) : IUserManagementService
{
    public async Task<IReadOnlyList<UserOverview>> ListAsync(CancellationToken cancellationToken = default)
    {
        var user = await db.Users.AsNoTracking().OrderBy(b => b.DisplayName).ToListAsync(cancellationToken);
        var roles = await (from ur in db.UserRoles
                           join r in db.Roles on ur.RoleId equals r.Id
                           select new { ur.UserId, r.Name }).ToListAsync(cancellationToken);
        var permissions = await db.UserClaims.Where(c => c.ClaimType == Permissions.ClaimType).ToListAsync(cancellationToken);

        return user.Select(b => Overview(
                b,
                roles.Where(r => r.UserId == b.Id).Select(r => r.Name!).FirstOrDefault() ?? "",
                permissions.Where(c => c.UserId == b.Id).Select(c => Enum.Parse<AccessRight>(c.ClaimValue!)).ToList()))
            .ToList();
    }

    public async Task<UserOverview?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return null;
        }

        var role = (await userManager.GetRolesAsync(user)).FirstOrDefault() ?? "";
        return Overview(user, role, await PermissionsOfAsync(user));
    }

    public async Task<AccountResult> ChangeRoleAsync(Guid id, string role, CancellationToken cancellationToken = default)
    {
        if (!Roles.All.Contains(role))
        {
            return AccountResult.Failed($"Unbekannte Rolle '{role}'.");
        }

        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return AccountResult.Failed("Konto nicht gefunden.");
        }

        var previous = await userManager.GetRolesAsync(user);
        if (previous.Contains(Roles.Admin) && role != Roles.Admin && await IsLastActiveAdminAsync(user, cancellationToken))
        {
            return AccountResult.Failed("Der letzte aktive Administrator kann seine Rolle nicht abgeben.");
        }

        await userManager.RemoveFromRolesAsync(user, previous);
        await userManager.AddToRoleAsync(user, role);

        // Permissions only apply to managers; switching to the manager role applies the default permissions.
        await ReplacePermissionsAsync(user, role == Roles.Manager ? Permissions.ManagerDefault : []);
        await audit.LogAsync(nameof(ApplicationUser), user.Id, AuditAction.Modified,
            [new FieldChange("Role", string.Join(", ", previous), role)], cancellationToken);
        return AccountResult.Ok(user.Id);
    }

    public async Task<AccountResult> SetPermissionsAsync(Guid id, IReadOnlyCollection<AccessRight> permissions, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return AccountResult.Failed("Konto nicht gefunden.");
        }

        if (!await userManager.IsInRoleAsync(user, Roles.Manager))
        {
            return AccountResult.Failed("Einzelrechte können nur für Verwalter festgelegt werden.");
        }

        var previous = await PermissionsOfAsync(user);
        await ReplacePermissionsAsync(user, permissions);
        await audit.LogAsync(nameof(ApplicationUser), user.Id, AuditAction.Modified,
            [new FieldChange("Permissions", string.Join(", ", previous.Order()), string.Join(", ", permissions.Distinct().Order()))], cancellationToken);
        return AccountResult.Ok(user.Id);
    }

    public async Task<AccountResult> LockAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return AccountResult.Failed("Konto nicht gefunden.");
        }

        if (await userManager.IsInRoleAsync(user, Roles.Admin) && await IsLastActiveAdminAsync(user, cancellationToken))
        {
            return AccountResult.Failed("Der letzte aktive Administrator kann nicht gesperrt werden.");
        }

        user.Locked = true;
        await userManager.UpdateAsync(user);
        await userManager.UpdateSecurityStampAsync(user); // existing sessions become invalid
        await db.Set<MagicLink>().Where(l => l.UserId == id && l.RedeemedAt == null).ExecuteDeleteAsync(cancellationToken);
        await audit.LogAsync(nameof(ApplicationUser), user.Id, AuditAction.Modified, [new FieldChange("Locked", "false", "true")], cancellationToken);
        return AccountResult.Ok(user.Id);
    }

    public async Task<AccountResult> UnlockAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return AccountResult.Failed("Konto nicht gefunden.");
        }

        user.Locked = false;
        await userManager.UpdateAsync(user);
        await userManager.SetLockoutEndDateAsync(user, null);
        await userManager.ResetAccessFailedCountAsync(user);
        await audit.LogAsync(nameof(ApplicationUser), user.Id, AuditAction.Modified, [new FieldChange("Locked", "true", "false")], cancellationToken);
        return AccountResult.Ok(user.Id);
    }

    private async Task<bool> IsLastActiveAdminAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var admins = await userManager.GetUsersInRoleAsync(Roles.Admin);
        return !admins.Any(a => a.Id != user.Id && !a.Locked);
    }

    private async Task<List<AccessRight>> PermissionsOfAsync(ApplicationUser user) =>
        (await userManager.GetClaimsAsync(user))
            .Where(c => c.Type == Permissions.ClaimType)
            .Select(c => Enum.Parse<AccessRight>(c.Value))
            .ToList();

    private async Task ReplacePermissionsAsync(ApplicationUser user, IReadOnlyCollection<AccessRight> permissions)
    {
        var previous = (await userManager.GetClaimsAsync(user)).Where(c => c.Type == Permissions.ClaimType).ToList();
        if (previous.Count > 0)
        {
            await userManager.RemoveClaimsAsync(user, previous);
        }

        if (permissions.Count > 0)
        {
            await userManager.AddClaimsAsync(user, permissions.Distinct().Order().Select(r => new Claim(Permissions.ClaimType, r.ToString())));
        }
    }

    private static UserOverview Overview(ApplicationUser b, string role, IReadOnlyList<AccessRight> permissions) => new(
        b.Id,
        b.DisplayName,
        b.Email ?? "",
        role,
        role == Roles.Admin ? Permissions.All : permissions,
        b.TwoFactorEnabled,
        b.PasswordHash is not null,
        b.Locked,
        b.EmailConfirmed,
        b.CreatedAt,
        b.LastSignInAt);
}
