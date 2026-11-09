using System.Security.Claims;
using Elocaro.Application.Accounts;
using Elocaro.Application.Audit;
using Elocaro.Infrastructure.Audit;
using Elocaro.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Elocaro.Infrastructure.Identity;

/// <remarks>
/// Role and permission changes and blocking renew the security stamp. That signs the user out, also in open interactive pages,
/// which only compare the stamp and would otherwise keep the old roles (<c>RevalidatingAuthenticationStateProvider</c>).
/// Accounts are reloaded on every change: in an interactive page the DbContext lives as long as the circuit.
/// </remarks>
internal sealed class UserManagement(ElocaroDbContext db, UserManager<ApplicationUser> userManager, IAccountService accounts, AuditWriter audit, TimeProvider clock) : IUserManagementService
{
    private const string NotAllowed = "Dafür fehlt die Berechtigung.";
    private const string AdminAccountsOnlyForAdmins = "Administratorkonten können nur Administratoren ändern.";

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

    public async Task<UserManagementActor?> ActorAsync(ClaimsPrincipal actor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!Guid.TryParse(userManager.GetUserId(actor), out var id)
            || await FindCurrentAsync(id, cancellationToken) is not { Locked: false } account)
        {
            return null;
        }

        var roles = await userManager.GetRolesAsync(account);
        if (roles.Contains(Roles.Admin))
        {
            return new UserManagementActor(id, true, Permissions.All);
        }

        var rights = await PermissionsOfAsync(account);
        return roles.Contains(Roles.Manager) && rights.Contains(AccessRight.UserManagement)
            ? new UserManagementActor(id, false, rights)
            : null;
    }

    public async Task<AccountResult> InviteAsync(ClaimsPrincipal actor, string email, string displayName, string role, CancellationToken cancellationToken = default)
    {
        if (await ActorAsync(actor, cancellationToken) is not { } current)
        {
            return AccountResult.Failed(NotAllowed);
        }

        if (role == Roles.Admin && !current.IsAdmin)
        {
            return AccountResult.Failed("Nur Administratoren können Administratoren einladen.");
        }

        return await accounts.InviteAsync(email, displayName, role, null, current.ManagerDefault, cancellationToken);
    }

    public async Task<AccountResult> ChangeRoleAsync(ClaimsPrincipal actor, Guid id, string role, CancellationToken cancellationToken = default)
    {
        if (!Roles.All.Contains(role))
        {
            return AccountResult.Failed($"Unbekannte Rolle '{role}'.");
        }

        if (await ActorAsync(actor, cancellationToken) is not { } current)
        {
            return AccountResult.Failed(NotAllowed);
        }

        if (!current.AssignableRoles.Contains(role))
        {
            return AccountResult.Failed("Nur Administratoren können die Rolle Administrator vergeben.");
        }

        var user = await FindCurrentAsync(id, cancellationToken);
        if (user is null)
        {
            return AccountResult.Failed("Konto nicht gefunden.");
        }

        var previous = await userManager.GetRolesAsync(user);
        if (!previous.All(current.MayManage))
        {
            return AccountResult.Failed(AdminAccountsOnlyForAdmins);
        }

        if (!current.IsAdmin && id == current.Id)
        {
            return AccountResult.Failed("Die eigene Rolle kann nur ein Administrator ändern.");
        }

        if (previous.Contains(Roles.Admin) && role != Roles.Admin && await IsLastActiveAdminAsync(user, cancellationToken))
        {
            return AccountResult.Failed("Der letzte aktive Administrator kann seine Rolle nicht abgeben.");
        }

        await userManager.RemoveFromRolesAsync(user, previous);
        await userManager.AddToRoleAsync(user, role);

        // Permissions only apply to managers; switching to the manager role applies the default permissions.
        await ReplacePermissionsAsync(user, role == Roles.Manager ? current.ManagerDefault : []);
        await EndSessionsAsync(user);
        await audit.LogAsync(nameof(ApplicationUser), user.Id, AuditAction.Modified,
            [new FieldChange("Role", string.Join(", ", previous), role)], cancellationToken);
        return AccountResult.Ok(user.Id);
    }

    public async Task<AccountResult> SetPermissionsAsync(ClaimsPrincipal actor, Guid id, IReadOnlyCollection<AccessRight> permissions, CancellationToken cancellationToken = default)
    {
        if (await ActorAsync(actor, cancellationToken) is not { } current)
        {
            return AccountResult.Failed(NotAllowed);
        }

        var user = await FindCurrentAsync(id, cancellationToken);
        if (user is null)
        {
            return AccountResult.Failed("Konto nicht gefunden.");
        }

        if (!current.IsAdmin && id == current.Id)
        {
            return AccountResult.Failed("Die eigenen Rechte kann nur ein Administrator ändern.");
        }

        if (!await userManager.IsInRoleAsync(user, Roles.Manager))
        {
            return AccountResult.Failed("Einzelrechte können nur für Verwalter festgelegt werden.");
        }

        // Permissions the account already has may stay (or be removed), even if the actor lacks them.
        var previous = await PermissionsOfAsync(user);
        if (permissions.Except(previous).Any(p => !current.MayGrant(p)))
        {
            return AccountResult.Failed("Sie können nur Rechte vergeben, die Sie selbst haben.");
        }

        await ReplacePermissionsAsync(user, permissions);
        await EndSessionsAsync(user);
        await audit.LogAsync(nameof(ApplicationUser), user.Id, AuditAction.Modified,
            [new FieldChange("Permissions", string.Join(", ", previous.Order()), string.Join(", ", permissions.Distinct().Order()))], cancellationToken);
        return AccountResult.Ok(user.Id);
    }

    public async Task<AccountResult> LockAsync(ClaimsPrincipal actor, Guid id, CancellationToken cancellationToken = default)
    {
        if (await ActorAsync(actor, cancellationToken) is not { } current)
        {
            return AccountResult.Failed(NotAllowed);
        }

        if (id == current.Id)
        {
            return AccountResult.Failed("Das eigene Konto kann nicht gesperrt werden.");
        }

        var user = await FindCurrentAsync(id, cancellationToken);
        if (user is null)
        {
            return AccountResult.Failed("Konto nicht gefunden.");
        }

        var isAdmin = await userManager.IsInRoleAsync(user, Roles.Admin);
        if (isAdmin && !current.IsAdmin)
        {
            return AccountResult.Failed(AdminAccountsOnlyForAdmins);
        }

        if (isAdmin && await IsLastActiveAdminAsync(user, cancellationToken))
        {
            return AccountResult.Failed("Der letzte aktive Administrator kann nicht gesperrt werden.");
        }

        user.Locked = true;
        await userManager.UpdateAsync(user);
        await userManager.UpdateSecurityStampAsync(user); // existing sessions become invalid
        await db.Set<MagicLink>().Where(l => l.UserId == id && l.RedeemedAt == null).ExecuteDeleteAsync(cancellationToken);
        var now = clock.GetUtcNow();
        await db.Set<AccessToken>().Where(t => t.UserId == id && t.RevokedAt == null).ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), cancellationToken); // not revived by unlocking
        await audit.LogAsync(nameof(ApplicationUser), user.Id, AuditAction.Modified, [new FieldChange("Locked", "false", "true")], cancellationToken);
        return AccountResult.Ok(user.Id);
    }

    public async Task<AccountResult> UnlockAsync(ClaimsPrincipal actor, Guid id, CancellationToken cancellationToken = default)
    {
        if (await ActorAsync(actor, cancellationToken) is not { } current)
        {
            return AccountResult.Failed(NotAllowed);
        }

        var user = await FindCurrentAsync(id, cancellationToken);
        if (user is null)
        {
            return AccountResult.Failed("Konto nicht gefunden.");
        }

        if (!current.IsAdmin && await userManager.IsInRoleAsync(user, Roles.Admin))
        {
            return AccountResult.Failed(AdminAccountsOnlyForAdmins);
        }

        user.Locked = false;
        await userManager.UpdateAsync(user);
        await userManager.SetLockoutEndDateAsync(user, null);
        await userManager.ResetAccessFailedCountAsync(user);
        await audit.LogAsync(nameof(ApplicationUser), user.Id, AuditAction.Modified, [new FieldChange("Locked", "true", "false")], cancellationToken);
        return AccountResult.Ok(user.Id);
    }

    private async Task<ApplicationUser?> FindCurrentAsync(Guid id, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is not null)
        {
            await db.Entry(user).ReloadAsync(cancellationToken);
        }

        return user;
    }

    /// <summary>
    /// Renews the security stamp, which ends all sessions. An account that has never signed in has none, and its invitation
    /// link depends on the stamp: it must survive adjusting the role or permissions right after inviting.
    /// </summary>
    private async Task EndSessionsAsync(ApplicationUser user)
    {
        if (user.LastSignInAt is null)
        {
            return;
        }

        var result = await userManager.UpdateSecurityStampAsync(user);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException("Security stamp could not be renewed: " + string.Join(", ", result.Errors.Select(e => e.Code)));
        }
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
