using Elocaro.Domain.Common;

namespace Elocaro.Domain.Operations;

/// <summary>
/// The OAuth client of this installation for the sign-in at Google (backup to Google Drive), entered in the app instead of the
/// environment. There is at most one. The client secret is encrypted by Data Protection and never shown again.
/// </summary>
[NoExport]
public sealed class GoogleClientSetting : Entity
{
    public const int MaxClientId = 200;

    public string ClientId { get; set; } = string.Empty;

    [Confidential]
    public string ProtectedClientSecret { get; set; } = string.Empty;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ClientId) || ClientId.Length > MaxClientId || ClientId.Any(char.IsWhiteSpace))
        {
            throw new RuleViolationException("Die Client-ID ist ungültig. Sie sieht aus wie 123456-abc.apps.googleusercontent.com.");
        }
    }
}
