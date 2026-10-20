using MietHahn.Domain.Common;

namespace MietHahn.Domain.Leases;

public enum ContractStatus
{
    /// <summary>PDF generated, waiting for the signed copy.</summary>
    Draft = 0,

    /// <summary>The signed contract is filed; the lease is contractually in force.</summary>
    Concluded = 1,

    /// <summary>Replaced by a new draft or discarded; stays for the record.</summary>
    Withdrawn = 2,
}

/// <summary>
/// The written contract of a lease: draft (generated PDF, with the exact template version) until the signed copy is
/// uploaded. At most one draft per lease is open.
/// </summary>
public sealed class LeaseContract : Entity
{
    private LeaseContract()
    {
    }

    public LeaseContract(Guid leaseId, Guid templateRevisionId, Guid draftFileId, DateOnly createdOn)
    {
        LeaseId = leaseId;
        TemplateRevisionId = templateRevisionId;
        DraftFileId = draftFileId;
        CreatedOn = createdOn;
    }

    public Guid LeaseId { get; private set; }

    public Guid TemplateRevisionId { get; private set; }

    /// <summary>The generated PDF (unsigned).</summary>
    public Guid DraftFileId { get; private set; }

    public DateOnly CreatedOn { get; private set; }

    public ContractStatus Status { get; private set; }

    public DateOnly? SignedOn { get; private set; }

    /// <summary>Document of the signed copy.</summary>
    public Guid? SignedDocumentId { get; private set; }

    public void Sign(DateOnly signedOn, Guid documentId)
    {
        if (Status != ContractStatus.Draft)
        {
            throw new RuleViolationException(Status == ContractStatus.Concluded ? "Der Vertrag ist bereits als unterschrieben abgelegt." : "Dieser Entwurf wurde ersetzt oder verworfen.");
        }

        if (signedOn < CreatedOn.AddDays(-1))
        {
            throw new RuleViolationException("Das Unterschriftsdatum liegt vor dem Entwurf.");
        }

        Status = ContractStatus.Concluded;
        SignedOn = signedOn;
        SignedDocumentId = documentId;
    }

    public void Withdraw()
    {
        if (Status != ContractStatus.Draft)
        {
            throw new RuleViolationException("Nur ein offener Entwurf lässt sich verwerfen.");
        }

        Status = ContractStatus.Withdrawn;
    }
}
