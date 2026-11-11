namespace Elocaro.Application.Operations;

/// <summary>What the person has to do at Google: open the address and enter the code.</summary>
public sealed record DeviceAuthorization(string DeviceCode, string UserCode, string VerificationUrl, int ExpiresInSeconds, int IntervalSeconds);

public enum DeviceAuthorizationState
{
    /// <summary>Not confirmed at Google yet: ask again after the interval.</summary>
    Pending,

    /// <summary>Google asks to wait longer between two requests.</summary>
    SlowDown,

    Authorized,

    /// <summary>The person refused at Google.</summary>
    Denied,

    /// <summary>The code ran out before it was confirmed.</summary>
    Expired,

    Failed,
}

/// <param name="Credentials">With <see cref="DeviceAuthorizationState.Authorized"/>: the credentials of the target (see <see cref="BackupProviders"/>).</param>
public sealed record DeviceAuthorizationResult(DeviceAuthorizationState State, IReadOnlyDictionary<string, string>? Credentials = null, string? Message = null);

public sealed record DeviceAuthorizationStart(DeviceAuthorization? Authorization, string? Error);

/// <summary>
/// Signing in to Google for the backup to Google Drive without any set-up on the part of the user: the device code flow (the person
/// opens an address on any device and enters a code). The app only gets access to the files it creates itself (<c>drive.file</c>).
/// </summary>
public interface IGoogleDriveAuthorization
{
    /// <summary>Whether an OAuth client is configured (in the app or in the environment); without one only the manual token works.</summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

    Task<DeviceAuthorizationStart> StartAsync(CancellationToken cancellationToken = default);

    Task<DeviceAuthorizationResult> PollAsync(string deviceCode, CancellationToken cancellationToken = default);
}
