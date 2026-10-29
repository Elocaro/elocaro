using System.ComponentModel.DataAnnotations;
using Elocaro.Application.Common;
using Elocaro.Application.Documents;
using Elocaro.Domain.Leases;

namespace Elocaro.Application.Leases;

/// <summary>
/// The written contract of a lease: generate a draft (PDF) from a contract template, then file the signed copy.
/// With the signed copy the contract counts as concluded.
/// </summary>
public interface ILeaseContracts
{
    Task<IReadOnlyList<ContractRow>> ListAsync(Guid leaseId, CancellationToken cancellationToken = default);

    /// <summary>The contract with the real data of the lease as PDF; stores nothing.</summary>
    Task<TemplatePreview> PreviewAsync(Guid templateId, Guid leaseId, CancellationToken cancellationToken = default);

    /// <summary>Generates and stores the draft. An earlier open draft of the lease is withdrawn. <see cref="Result.Id"/> is the contract.</summary>
    Task<Result> CreateDraftAsync(Guid leaseId, Guid templateId, CancellationToken cancellationToken = default);

    /// <summary>Files the signed copy as document of the lease and concludes the contract.</summary>
    Task<Result> ConcludeAsync(Guid contractId, SignedContractInput input, string fileName, Stream content, CancellationToken cancellationToken = default);

    Task<Result> WithdrawAsync(Guid contractId, CancellationToken cancellationToken = default);
}

public sealed class SignedContractInput
{
    [Required(ErrorMessage = "Bitte das Datum der Unterschrift angeben.")]
    public DateOnly? SignedOn { get; set; }

    /// <summary>Make the signed contract visible in the tenant portal.</summary>
    public bool ShareWithTenants { get; set; }
}

public sealed record ContractRow(
    Guid Id,
    ContractStatus Status,
    DateOnly CreatedOn,
    string TemplateName,
    int TemplateRevision,
    Guid DraftFileId,
    DateOnly? SignedOn,
    Guid? SignedDocumentId);
