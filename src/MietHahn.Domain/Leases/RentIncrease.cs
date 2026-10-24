using MietHahn.Domain.Common;

namespace MietHahn.Domain.Leases;

public enum RentIncreaseKind
{
    /// <summary>Demand for consent to the local comparative rent (§ 558 BGB).</summary>
    Comparative = 0,

    /// <summary>Declaration of an index adjustment (§ 557b BGB).</summary>
    Index = 1,

    /// <summary>Information about an agreed step before its date (§ 557a BGB).</summary>
    Graduated = 2,

    /// <summary>Adjustment of the operating cost flat rate (§ 560 Abs. 1 BGB).</summary>
    FlatRate = 3,

    /// <summary>Increase after modernisation (§§ 559 to 559c BGB); effective without consent.</summary>
    Modernisation = 4,
}

public enum RentIncreaseStatus
{
    /// <summary>Letter generated, not yet received by the tenant.</summary>
    Draft = 0,

    /// <summary>Received; for a demand to consent: waiting for the tenant.</summary>
    Declared = 1,

    /// <summary>The tenant consented; the new rent is in the lease.</summary>
    Agreed = 2,

    /// <summary>The tenant refused or the period ran out.</summary>
    Refused = 3,

    /// <summary>Index or flat rate: effective without consent, the new amount is in the lease.</summary>
    InForce = 4,

    Withdrawn = 5,
}

public enum ComparativeBasis
{
    /// <summary>Rent index (Mietspiegel), § 558a Abs. 2 Nr. 1.</summary>
    RentIndex = 0,

    /// <summary>At least three comparable flats, § 558a Abs. 2 Nr. 4.</summary>
    Comparables = 1,

    /// <summary>Expert opinion, § 558a Abs. 2 Nr. 3.</summary>
    Appraisal = 2,
}

/// <summary>
/// A rent increase of a lease from letter to the new amount in the lease. The letter is generated with the draft (with the exact
/// template version); the dates follow from the day of receipt, which is entered later.
/// </summary>
public sealed class RentIncrease : Entity
{
    private RentIncrease()
    {
        Summary = "";
    }

    public RentIncrease(Guid leaseId, RentIncreaseKind kind, decimal currentAmount, decimal newAmount, DateOnly plannedDeclaration, DateOnly effectiveFrom, DateOnly? consentDeadline, string summary)
    {
        if (newAmount <= currentAmount)
        {
            throw new RuleViolationException("Der neue Betrag muss höher sein als der bisherige.");
        }

        LeaseId = leaseId;
        Kind = kind;
        CurrentAmount = currentAmount;
        NewAmount = newAmount;
        PlannedDeclaration = plannedDeclaration;
        EffectiveFrom = effectiveFrom;
        ConsentDeadline = consentDeadline;
        Summary = summary;
    }

    public Guid LeaseId { get; private set; }

    public RentIncreaseKind Kind { get; private set; }

    public RentIncreaseStatus Status { get; private set; }

    /// <summary>Base rent (comparative, index, graduated) or flat rate before the change.</summary>
    public decimal CurrentAmount { get; private set; }

    public decimal NewAmount { get; private set; }

    /// <summary>Day of receipt the letter was prepared for.</summary>
    public DateOnly PlannedDeclaration { get; private set; }

    public DateOnly EffectiveFrom { get; private set; }

    /// <summary>Last day to consent (demand to consent only).</summary>
    public DateOnly? ConsentDeadline { get; private set; }

    public DateOnly? DeclaredOn { get; private set; }

    public DateOnly? ConsentOn { get; private set; }

    /// <summary>Short description for lists, e.g. the basis of the demand.</summary>
    public string Summary { get; private set; }

    public ComparativeBasis? Basis { get; set; }

    public decimal? ComparativeRentAmount { get; set; }

    public int? CapPercent { get; set; }

    public decimal? CapLimit { get; set; }

    /// <summary>Index adjustment: the index values the new rent is based on.</summary>
    public DateOnly? IndexOldMonth { get; set; }

    public decimal? IndexOldValue { get; set; }

    public DateOnly? IndexNewMonth { get; set; }

    public decimal? IndexNewValue { get; set; }

    /// <summary>Modernisation: costs spent for the flat and the share of maintenance deducted from them.</summary>
    public decimal? ModernisationCosts { get; set; }

    public decimal? MaintenanceShare { get; set; }

    /// <summary>Modernisation: not announced or over 10 % dearer, so the increase starts six months later (§ 559b Abs. 2 Satz 2).</summary>
    public bool EffectiveExtended { get; set; }

    public Guid? TemplateRevisionId { get; set; }

    /// <summary>The letter in the document library.</summary>
    public Guid? DocumentId { get; set; }

    public string? Note { get; set; }

    /// <summary>Corrects the new amount while the letter is still a draft (the rent changed since it was prepared).</summary>
    public void Restate(decimal newAmount)
    {
        if (Status != RentIncreaseStatus.Draft || newAmount <= CurrentAmount)
        {
            throw new RuleViolationException("Der neue Betrag muss höher sein als der bisherige.");
        }

        NewAmount = newAmount;
    }

    /// <summary>The letter has been received. Dates are recalculated from the actual day of receipt.</summary>
    public void Declare(DateOnly received, DateOnly effectiveFrom, DateOnly? consentDeadline)
    {
        if (Status != RentIncreaseStatus.Draft)
        {
            throw new RuleViolationException("Der Zugang wurde bereits erfasst oder das Schreiben ist verworfen.");
        }

        DeclaredOn = received;
        EffectiveFrom = effectiveFrom;
        ConsentDeadline = consentDeadline;
        Status = Kind is RentIncreaseKind.Index or RentIncreaseKind.FlatRate or RentIncreaseKind.Modernisation ? RentIncreaseStatus.InForce : RentIncreaseStatus.Declared;
    }

    public void Agree(DateOnly on)
    {
        if (Kind != RentIncreaseKind.Comparative || Status != RentIncreaseStatus.Declared)
        {
            throw new RuleViolationException("Eine Zustimmung gibt es nur zu einem zugegangenen Verlangen auf die Vergleichsmiete.");
        }

        if (DeclaredOn is { } declared && on < declared)
        {
            throw new RuleViolationException("Die Zustimmung kann nicht vor dem Zugang liegen.");
        }

        if (ConsentDeadline is { } deadline && on > deadline)
        {
            throw new RuleViolationException($"Die Zustimmungsfrist ist am {deadline:dd.MM.yyyy} abgelaufen. Eine spätere Zustimmung ist eine freie Vereinbarung: bitte die neue Miete als Mietbestandteil mit dem vereinbarten Datum erfassen.");
        }

        ConsentOn = on;
        Status = RentIncreaseStatus.Agreed;
    }

    public void Refuse()
    {
        if (Kind != RentIncreaseKind.Comparative || Status != RentIncreaseStatus.Declared)
        {
            throw new RuleViolationException("Nur ein zugegangenes Verlangen auf die Vergleichsmiete kann abgelehnt sein.");
        }

        Status = RentIncreaseStatus.Refused;
    }

    public void Withdraw()
    {
        if (Status is not (RentIncreaseStatus.Draft or RentIncreaseStatus.Declared) || (Status == RentIncreaseStatus.Declared && Kind != RentIncreaseKind.Comparative))
        {
            throw new RuleViolationException("Dieses Schreiben ist schon wirksam und kann nicht verworfen werden.");
        }

        Status = RentIncreaseStatus.Withdrawn;
    }
}
