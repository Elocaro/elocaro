using MietHahn.Domain.Common;

namespace MietHahn.Domain.Leases;

public enum SignerRole
{
    Tenant = 0,
    Landlord = 1,
}

/// <summary>
/// Request for a simple electronic signature of a contract draft: one per signer, reached by a personal link. The signature
/// records the typed name, an optional drawn signature, the time, the IP address and the SHA-256 hash of the document that was
/// shown. It is not a qualified electronic signature (§ 126a BGB).
/// </summary>
[NoAudit]
public sealed class SignatureRequest : Entity
{
    private SignatureRequest()
    {
        SignerName = "";
        TokenHash = "";
    }

    public SignatureRequest(Guid contractId, SignerRole role, string signerName, string? email, Guid? personId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signerName);
        ContractId = contractId;
        Role = role;
        SignerName = signerName.Trim();
        Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        PersonId = personId;
        TokenHash = "";
    }

    public Guid ContractId { get; private set; }

    public SignerRole Role { get; private set; }

    public string SignerName { get; private set; }

    public string? Email { get; private set; }

    public Guid? PersonId { get; private set; }

    /// <summary>SHA-256 (hex) of the token in the link; the token itself is never stored.</summary>
    public string TokenHash { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? SentAt { get; set; }

    public DateTimeOffset? SignedAt { get; private set; }

    /// <summary>The name the signer typed (also the signature if nothing was drawn).</summary>
    public string? TypedName { get; private set; }

    /// <summary>PNG of the drawn signature, if any.</summary>
    public Guid? SignatureFileId { get; private set; }

    /// <summary>SHA-256 (hex) of the contract PDF that the signer saw.</summary>
    public string? DocumentHash { get; private set; }

    public string? IpAddress { get; private set; }

    public string? UserAgent { get; private set; }

    public bool IsSigned => SignedAt is not null;

    public bool IsExpired(DateTimeOffset now) => !IsSigned && now > ExpiresAt;

    /// <summary>A new link replaces the old one.</summary>
    public void IssueToken(string tokenHash, DateTimeOffset expiresAt)
    {
        if (IsSigned)
        {
            throw new RuleViolationException("Diese Person hat bereits unterschrieben.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        TokenHash = tokenHash;
        ExpiresAt = expiresAt;
    }

    public void Sign(DateTimeOffset at, string typedName, Guid? signatureFileId, string documentHash, string? ipAddress, string? userAgent)
    {
        if (IsSigned)
        {
            throw new RuleViolationException("Der Vertrag wurde bereits unterschrieben.");
        }

        if (IsExpired(at))
        {
            throw new RuleViolationException("Der Link ist abgelaufen. Bitte lassen Sie sich einen neuen Link schicken.");
        }

        if (string.IsNullOrWhiteSpace(typedName))
        {
            throw new RuleViolationException("Bitte geben Sie Ihren Namen ein.");
        }

        SignedAt = at;
        TypedName = typedName.Trim();
        SignatureFileId = signatureFileId;
        DocumentHash = documentHash;
        IpAddress = ipAddress;
        UserAgent = userAgent is { Length: > 300 } ? userAgent[..300] : userAgent;
    }
}
