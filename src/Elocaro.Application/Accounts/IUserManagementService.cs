namespace Elocaro.Application.Accounts;

/// <summary>Management of existing user accounts (admin or the "Benutzerverwaltung" permission).</summary>
public interface IUserManagementService
{
    Task<IReadOnlyList<UserOverview>> ListAsync(CancellationToken cancellationToken = default);

    Task<UserOverview?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Replaces the account's role. The last active admin cannot lose their role.</summary>
    Task<AccountResult> ChangeRoleAsync(Guid id, string role, CancellationToken cancellationToken = default);

    /// <summary>Sets the individual permissions of a manager.</summary>
    Task<AccountResult> SetPermissionsAsync(Guid id, IReadOnlyCollection<AccessRight> permissions, CancellationToken cancellationToken = default);

    /// <summary>Locks the account and ends all sessions. The last active admin cannot be locked.</summary>
    Task<AccountResult> LockAsync(Guid id, CancellationToken cancellationToken = default);

    Task<AccountResult> UnlockAsync(Guid id, CancellationToken cancellationToken = default);
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
