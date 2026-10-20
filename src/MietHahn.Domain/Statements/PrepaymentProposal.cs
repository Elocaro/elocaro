using MietHahn.Domain.Common;
using MietHahn.Domain.Leases;

namespace MietHahn.Domain.Statements;

public enum ProposalStatus
{
    Open = 0,
    Applied = 1,
    Declined = 2,
}

/// <summary>
/// Proposal to adjust a monthly prepayment after a statement (§ 560 para. 4 BGB). It is part of the letter; a new rent component
/// is created only after the landlord has confirmed it.
/// </summary>
public sealed class PrepaymentProposal : Entity
{
    private PrepaymentProposal()
    {
    }

    public PrepaymentProposal(Guid dispatchId, Guid leaseId, RentComponentType component, decimal current, decimal proposed, DateOnly effectiveFrom)
    {
        if (component is not (RentComponentType.OperatingCostPrepayment or RentComponentType.HeatingCostPrepayment))
        {
            throw new RuleViolationException("Nur Vorauszahlungen für Betriebs- oder Heizkosten lassen sich anpassen.");
        }

        if (proposed < 0)
        {
            throw new RuleViolationException("Die Vorauszahlung darf nicht negativ sein.");
        }

        DispatchId = dispatchId;
        LeaseId = leaseId;
        Component = component;
        Current = current;
        Proposed = proposed;
        EffectiveFrom = effectiveFrom;
    }

    public Guid DispatchId { get; private set; }

    public Guid LeaseId { get; private set; }

    public RentComponentType Component { get; private set; }

    public decimal Current { get; private set; }

    public decimal Proposed { get; private set; }

    public DateOnly EffectiveFrom { get; private set; }

    public ProposalStatus Status { get; private set; }

    public DateOnly? DecidedOn { get; private set; }

    public void Apply(DateOnly today)
    {
        EnsureOpen();
        Status = ProposalStatus.Applied;
        DecidedOn = today;
    }

    public void Decline(DateOnly today)
    {
        EnsureOpen();
        Status = ProposalStatus.Declined;
        DecidedOn = today;
    }

    private void EnsureOpen()
    {
        if (Status != ProposalStatus.Open)
        {
            throw new RuleViolationException("Der Vorschlag wurde bereits entschieden.");
        }
    }
}
