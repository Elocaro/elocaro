namespace Elocaro.Application.Audit;

/// <summary>Security-relevant account events that appear in the audit log.</summary>
public enum SecurityEvent
{
    SignedIn,
    SignInFailed,
    TwoFactorFailed,
    RecoveryCodeUsed,
    RecoveryCodeFailed,
    LockedOut,
    PasswordReset,
    MagicLinkSignedIn,
}

/// <summary>Writes sign-in and lockout events to the audit log (never passwords, codes or tokens).</summary>
public interface ISecurityAudit
{
    /// <param name="kind">What happened.</param>
    /// <param name="email">Entered or known address; shortened and cleaned of control characters. <c>null</c> if unknown.</param>
    /// <param name="userId">Account, if it exists.</param>
    Task LogAsync(SecurityEvent kind, string? email, Guid? userId, CancellationToken cancellationToken = default);
}
