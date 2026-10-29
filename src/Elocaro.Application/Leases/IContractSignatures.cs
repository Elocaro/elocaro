using System.ComponentModel.DataAnnotations;
using Elocaro.Application.Common;
using Elocaro.Application.Files;
using Elocaro.Domain.Leases;

namespace Elocaro.Application.Leases;

/// <summary>
/// Simple electronic signature of a contract draft: every signer (tenants, optionally the landlord) gets a personal link, reads the
/// PDF and signs by typing the name and optionally drawing. With the last signature the draft is filed as signed contract, with
/// a protocol page (who, when, IP address, SHA-256 of the document).
/// </summary>
public interface IContractSignatures
{
    Task<IReadOnlyList<SignatureRow>> ListAsync(Guid contractId, CancellationToken cancellationToken = default);

    /// <summary>Creates the requests of an open draft, sends mails to the tenants with an address and returns the links.</summary>
    Task<SignatureLinks> StartAsync(Guid contractId, bool includeLandlord, CancellationToken cancellationToken = default);

    /// <summary>New link for one signer (the old one stops working); the tenant gets a mail again if there is an address.</summary>
    Task<SignatureLinks> ReissueAsync(Guid requestId, CancellationToken cancellationToken = default);

    /// <summary>Removes the open requests; only possible while nobody has signed.</summary>
    Task<Result> CancelAsync(Guid contractId, CancellationToken cancellationToken = default);

    /// <summary>All signed but not yet filed (e.g. the PDF service was down): files the signed contract.</summary>
    Task<Result> CompleteAsync(Guid contractId, CancellationToken cancellationToken = default);

    /// <summary>What the signer sees; <c>null</c> for an unknown link.</summary>
    Task<SigningView?> ForSigningAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>The draft PDF of the link, or <c>null</c>.</summary>
    Task<OpenedFile?> OpenDocumentAsync(string token, CancellationToken cancellationToken = default);

    Task<SigningResult> SignAsync(string token, SigningInput input, SigningEvidence evidence, CancellationToken cancellationToken = default);
}

public sealed class SigningInput
{
    [Required(ErrorMessage = "Bitte geben Sie Ihren vollständigen Namen ein.")]
    [StringLength(200)]
    public string? TypedName { get; set; }

    /// <summary>Drawn signature (PNG, base64); optional.</summary>
    public string? SignatureData { get; set; }

    [Range(typeof(bool), "true", "true", ErrorMessage = "Bitte bestätigen Sie, dass Sie den Vertrag gelesen haben.")]
    public bool Accepted { get; set; }
}

/// <summary>What the server notes about the signing request.</summary>
public sealed record SigningEvidence(string? IpAddress, string? UserAgent);

public sealed record SignatureRow(Guid Id, SignerRole Role, string SignerName, string? Email, DateTimeOffset? SentAt, DateTimeOffset ExpiresAt, DateTimeOffset? SignedAt, bool Expired, string? DocumentHash);

public sealed record SignatureLink(Guid RequestId, string SignerName, SignerRole Role, string Url, bool Mailed);

public sealed record SignatureLinks(bool Succeeded, IReadOnlyList<string> Errors, IReadOnlyList<SignatureLink> Links);

public sealed record SigningView(
    string SignerName,
    SignerRole Role,
    string Title,
    string TenantNames,
    string RentalUnit,
    string Property,
    DateTimeOffset ExpiresAt,
    bool IsSigned,
    bool Expired,
    bool ContractOpen);

/// <param name="AllSigned">This was the last signature and the contract is filed.</param>
public sealed record SigningResult(bool Succeeded, IReadOnlyList<string> Errors, bool AllSigned, string? Note = null);
