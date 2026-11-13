using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Elocaro.Domain.Admin;

namespace Elocaro.Application.Emergency;

/// <summary>
/// The emergency folder: one encrypted HTML file with everything relatives need to carry on the rental business even if
/// Elocaro or the server is gone. The password is only used to derive the key; it is never stored or logged.
/// </summary>
public interface IEmergencyFolder
{
    Task<EmergencyFolderState> StateAsync(CancellationToken cancellationToken = default);

    /// <summary>Saves the landlord's own notes (and nothing else).</summary>
    Task<EmergencyFolderOutcome> SaveNotesAsync(EmergencyFolderNotes notes, CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds the folder in memory, encrypts it and records the creation (audit entry, last-created date).
    /// Nothing unencrypted is written to disk.
    /// </summary>
    Task<EmergencyFolderResult> CreateAsync(EmergencyFolderRequest request, ClaimsPrincipal user, CancellationToken cancellationToken = default);
}

public static class EmergencyFolderLimits
{
    /// <summary>Minimum length of the password (same as for accounts).</summary>
    public const int MinPasswordLength = 12;

    /// <summary>Maximum length of the password (protects the server from absurd input).</summary>
    public const int MaxPasswordLength = 256;

    /// <summary>Maximum size of the unencrypted content including embedded documents.</summary>
    public const long MaxContentBytes = 100L * 1024 * 1024;
}

public sealed class EmergencyFolderNotes
{
    [StringLength(EmergencyFolder.MaxLength, ErrorMessage = "Der Text ist zu lang.")]
    public string? ImportantContacts { get; set; }

    [StringLength(EmergencyFolder.MaxLength, ErrorMessage = "Der Text ist zu lang.")]
    public string? DocumentLocation { get; set; }

    [StringLength(EmergencyFolder.MaxLength, ErrorMessage = "Der Text ist zu lang.")]
    public string? FurtherNotes { get; set; }
}

public sealed record EmergencyFolderState(EmergencyFolderNotes Notes, DateTimeOffset? LastCreatedAt);

/// <summary>Why saving or creating failed; each value has a fixed user text on the page.</summary>
public enum EmergencyFolderProblem
{
    None = 0,
    NotesTooLong = 1,
    PasswordTooShort = 2,
    PasswordsDiffer = 3,
    TooLarge = 4,
}

public sealed record EmergencyFolderOutcome(EmergencyFolderProblem Problem)
{
    public bool Succeeded => Problem == EmergencyFolderProblem.None;
}

/// <summary>The request deliberately overrides <c>ToString</c>, so the password cannot end up in a log by accident.</summary>
/// <param name="Password">Chosen by the user, at least <see cref="EmergencyFolderLimits.MinPasswordLength"/> characters.</param>
/// <param name="PasswordConfirmation">The password entered a second time.</param>
/// <param name="IncludeLeaseDocuments">Embed the signed contracts of current leases (counts towards the size limit).</param>
public sealed record EmergencyFolderRequest(string Password, string PasswordConfirmation, bool IncludeLeaseDocuments)
{
    public override string ToString() => nameof(EmergencyFolderRequest);
}

/// <param name="Content">The encrypted HTML file; <c>null</c> on failure.</param>
public sealed record EmergencyFolderResult(EmergencyFolderProblem Problem, string FileName, byte[]? Content)
{
    public bool Succeeded => Problem == EmergencyFolderProblem.None && Content is not null;
}
