using Elocaro.Domain.Common;

namespace Elocaro.Domain.Admin;

/// <summary>
/// The landlord's own notes for the emergency folder (exactly one record) and when the folder was last created.
/// The texts are stored like all other business data, unencrypted in the database; they must not contain passwords.
/// </summary>
public sealed class EmergencyFolder : Entity
{
    public const int MaxLength = 4000;

    /// <summary>Who to call: family, tax advisor, lawyer, caretaker, bank, insurance broker.</summary>
    public string? ImportantContacts { get; set; }

    /// <summary>Where the original documents, keys and contracts are kept.</summary>
    public string? DocumentLocation { get; set; }

    /// <summary>Anything else a successor should know.</summary>
    public string? FurtherNotes { get; set; }

    /// <summary>When an emergency folder was last created; <c>null</c> = never.</summary>
    public DateTimeOffset? LastCreatedAt { get; set; }
}
