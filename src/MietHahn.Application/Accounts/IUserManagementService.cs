namespace MietHahn.Application.Accounts;

/// <summary>Verwaltung bestehender Benutzerkonten (Admin bzw. Recht „Benutzerverwaltung“).</summary>
public interface IUserManagementService
{
    Task<IReadOnlyList<UserOverview>> ListAsync(CancellationToken cancellationToken = default);

    Task<UserOverview?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Ersetzt die Rolle des Kontos. Der letzte aktive Admin kann seine Rolle nicht verlieren.</summary>
    Task<AccountResult> ChangeRoleAsync(Guid id, string role, CancellationToken cancellationToken = default);

    /// <summary>Setzt die Einzelrechte eines Verwalters.</summary>
    Task<AccountResult> SetPermissionsAsync(Guid id, IReadOnlyCollection<AccessRight> permissions, CancellationToken cancellationToken = default);

    /// <summary>Sperrt das Konto und beendet alle Sitzungen. Der letzte aktive Admin kann nicht gesperrt werden.</summary>
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
