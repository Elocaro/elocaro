namespace MietHahn.Application.Accounts;

/// <summary>Creation and invitation of user accounts as well as passwordless sign-in (magic link).</summary>
public interface IAccountService
{
    /// <summary>The initial setup is only possible as long as there is no user yet.</summary>
    Task<bool> InitialSetupPossibleAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates the first administrator.</summary>
    Task<AccountResult> CreateFirstAdminAsync(string email, string displayName, string password, CancellationToken cancellationToken = default);

    /// <summary>
    /// Invites a user. Roles with a password receive a link to set the password (valid 7 days),
    /// tenants and tradespeople a sign-in link (valid 7 days).
    /// </summary>
    Task<AccountResult> InviteAsync(string email, string displayName, string role, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a sign-in link (valid 15 minutes) if a matching, authorized account exists.
    /// Deliberately gives no information about whether the address is known.
    /// </summary>
    Task RequestMagicLinkAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>Redeems a sign-in link (once) and returns the user id, or <c>null</c>.</summary>
    Task<Guid?> RedeemMagicLinkAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>Sends a password reset link (valid 2 hours); without revealing whether the address is known.</summary>
    Task RequestPasswordResetAsync(string email, CancellationToken cancellationToken = default);
}

public sealed record AccountResult(bool Succeeded, Guid? UserId, IReadOnlyList<string> Errors)
{
    public static AccountResult Ok(Guid id) => new(true, id, []);

    public static AccountResult Failed(params string[] errors) => new(false, null, errors);
}
