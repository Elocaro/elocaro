using Elocaro.Application.Common;
using Elocaro.Application.Documents;
using Elocaro.Domain.Leases;

namespace Elocaro.Application.Leases;

/// <summary>
/// Handover protocol at move-in and move-out: rooms from the master data, condition, defects with photos, keys and meter readings.
/// Signing (on the device or as a signed printout) files the PDF and enters the meter readings as interim readings.
/// </summary>
public interface IHandovers
{
    /// <summary>The protocol of the lease for the type, or <c>null</c> if there is none yet.</summary>
    Task<HandoverView?> GetAsync(Guid leaseId, HandoverType type, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates the protocol with a prefilled content (rooms, keys, meters). At move-out the content of the move-in protocol is the
    /// starting point, so changes show up in the comparison.
    /// </summary>
    Task<Result> CreateAsync(Guid leaseId, HandoverType type, DateOnly date, CancellationToken cancellationToken = default);

    Task<Result> SaveAsync(Guid id, DateOnly date, HandoverContent content, CancellationToken cancellationToken = default);

    /// <summary>Stores a photo (processed, without metadata). <see cref="Result.Id"/> is the file to attach to a defect or reading.</summary>
    Task<Result> AddPhotoAsync(Guid id, string fileName, Stream content, CancellationToken cancellationToken = default);

    /// <summary>The protocol as PDF in its current state; stores nothing.</summary>
    Task<TemplatePreview> PreviewAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Signs on the device: files the PDF with the signatures as document, enters the meter readings and at move-out the new address.
    /// Warnings about the readings (backward running, outliers) must be confirmed once.
    /// </summary>
    Task<HandoverSignResult> SignAsync(Guid id, HandoverSignature signature, bool confirmWarnings, CancellationToken cancellationToken = default);

    /// <summary>Files the scan of the signed printout instead of a signature on the device.</summary>
    Task<HandoverSignResult> SignWithScanAsync(Guid id, string signedByName, string fileName, Stream scan, bool confirmWarnings, CancellationToken cancellationToken = default);

    /// <summary>Only drafts can be deleted.</summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed class HandoverSignature
{
    public string SignedByName { get; set; } = "";

    /// <summary>PNG of the signature of the tenant(s).</summary>
    public byte[]? TenantSignature { get; set; }

    /// <summary>PNG of the signature of the landlord (optional).</summary>
    public byte[]? LandlordSignature { get; set; }
}

public sealed record HandoverSignResult(bool Succeeded, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings, Guid? DocumentId = null)
{
    public bool NeedsConfirmation => !Succeeded && Warnings.Count > 0 && Errors.Count == 0;
}

public sealed record HandoverView(
    Guid Id,
    Guid LeaseId,
    HandoverType Type,
    DateOnly Date,
    HandoverStatus Status,
    HandoverContent Content,
    string RentalUnit,
    string Property,
    string Tenants,
    DateOnly LeaseStart,
    DateOnly? LeaseEnd,
    string? SignedByName,
    DateTimeOffset? SignedAt,
    Guid? DocumentFileId,
    Guid? DocumentId,
    IReadOnlyList<HandoverDifference> Differences);
