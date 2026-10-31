using System.Security.Claims;

namespace Elocaro.Application.Accounts;

/// <summary>
/// Management of user accounts (admin or the "Benutzerverwaltung" permission). Changing methods check <c>actor</c> against the
/// database and enforce the limits of <see cref="UserManagementActor"/>, whatever the calling page offers.
/// </summary>
public interface IUserManagementService
{
    Task<IReadOnlyList<UserOverview>> ListAsync(CancellationToken cancellationToken = default);

    Task<UserOverview?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>What the signed-in account may do here; <c>null</c> if it may not manage accounts at all.</summary>
    Task<UserManagementActor?> ActorAsync(ClaimsPrincipal actor, CancellationToken cancellationToken = default);

    /// <summary>Invites a user with a freely chosen role (see <see cref="IAccountService"/> for the invitation itself).</summary>
    Task<AccountResult> InviteAsync(ClaimsPrincipal actor, string email, string displayName, string role, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the account's role and ends its sessions. The last active admin cannot lose their role.
    /// A new manager receives the default permissions, as far as the actor has them.
    /// </summary>
    Task<AccountResult> ChangeRoleAsync(ClaimsPrincipal actor, Guid id, string role, CancellationToken cancellationToken = default);

    /// <summary>Sets the individual permissions of a manager and ends their sessions.</summary>
    Task<AccountResult> SetPermissionsAsync(ClaimsPrincipal actor, Guid id, IReadOnlyCollection<AccessRight> permissions, CancellationToken cancellationToken = default);

    /// <summary>Locks the account and ends all sessions. The last active admin cannot be locked.</summary>
    Task<AccountResult> LockAsync(ClaimsPrincipal actor, Guid id, CancellationToken cancellationToken = default);

    Task<AccountResult> UnlockAsync(ClaimsPrincipal actor, Guid id, CancellationToken cancellationToken = default);
}

/// <summary>
/// The account performing a change. Admins may do everything. Anyone else may not hand out the admin role, not touch admin
/// accounts, not change their own role or permissions, and not grant permissions they lack themselves.
/// </summary>
public sealed record UserManagementActor(Guid Id, bool IsAdmin, IReadOnlyList<AccessRight> Rights)
{
    public IReadOnlyList<string> AssignableRoles => IsAdmin ? Roles.All : Roles.All.Where(r => r != Roles.Admin).ToList();

    /// <summary>Permissions of a manager created by this actor.</summary>
    public IReadOnlyList<AccessRight> ManagerDefault => Permissions.ManagerDefault.Intersect(Rights).ToList();

    public bool MayManage(string role) => IsAdmin || role != Roles.Admin;

    public bool MayChangeAccess(Guid id, string role) => MayManage(role) && (IsAdmin || id != Id);

    public bool MayGrant(AccessRight permission) => Rights.Contains(permission);
}

public sealed record UserOverview(
    Guid Id,
    string DisplayName,
    string Email,
    string Role,
    IReadOnlyList<AccessRight> Permissions,
    bool TwoFactorEnabled,
    bool HasPassword,
    bool Locked,
    bool InvitationAccepted,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastSignInAt);
