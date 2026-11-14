using Elocaro.Domain.Common;

namespace Elocaro.Domain.Finance;

public enum RentReductionStatus
{
    /// <summary>The tenant has announced the reduction; no effect on the account yet.</summary>
    Announced = 0,

    /// <summary>The landlord accepts the reduction: the account gets a credit for each month.</summary>
    Acknowledged = 1,

    /// <summary>The landlord disputes the reduction: no credit, but the disputed amount is not dunned.</summary>
    Disputed = 2,

    /// <summary>The defect is remedied or the matter is settled; an acknowledged reduction keeps applying up to its end date.</summary>
    Ended = 3,
}

/// <summary>
/// Rent reduction of a lease (§ 536 BGB): a period, a quota in percent of the gross rent or a fixed monthly amount, and
/// a status. The monthly credits and the dunning exemption follow from it; the amount is calculated by <see cref="AmountFor"/>.
/// </summary>
public sealed class RentReduction : Entity
{
    private RentReduction()
    {
        Description = "";
    }

    public RentReduction(Guid leaseId, string description, DateOnly from, DateOnly? to, decimal? percent, decimal? monthlyAmount, RentReductionStatus status, DateOnly? today = null)
    {
        LeaseId = leaseId;
        Description = description;
        Status = status;
        Apply(description, from, to, percent, monthlyAmount, status, today);
    }

    public Guid LeaseId { get; private set; }

    public string Description { get; private set; }

    public DateOnly From { get; private set; }

    /// <summary>Last day of the reduction; empty = continues until it is ended.</summary>
    public DateOnly? To { get; private set; }

    /// <summary>Quota in percent of the gross rent (rent including prepayments, BGH XII ZR 225/03); alternative to <see cref="MonthlyAmount"/>.</summary>
    public decimal? Percent { get; private set; }

    /// <summary>Fixed amount per month; alternative to <see cref="Percent"/>.</summary>
    public decimal? MonthlyAmount { get; private set; }

    public RentReductionStatus Status { get; private set; }

    /// <summary>Day the landlord acknowledged the reduction; an ended reduction only counts as granted if this is set.</summary>
    public DateOnly? AcknowledgedOn { get; private set; }

    /// <summary>Ticket with the defect and its evidence (plain reference, the ticket module is not a dependency).</summary>
    public Guid? TicketId { get; set; }

    public string? Note { get; set; }

    public Period Period => new(From, To);

    /// <summary>Whether the reduction lowers the rent (credit per month).</summary>
    public bool Grants => Status == RentReductionStatus.Acknowledged || (Status == RentReductionStatus.Ended && AcknowledgedOn is not null);

    public bool IsDisputed => Status == RentReductionStatus.Disputed;

    public void Apply(string description, DateOnly from, DateOnly? to, decimal? percent, decimal? monthlyAmount, RentReductionStatus status, DateOnly? today = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        if (to < from)
        {
            throw new RuleViolationException("Das Ende der Minderung liegt vor dem Beginn.");
        }

        if (percent is null == monthlyAmount is null)
        {
            throw new RuleViolationException("Bitte entweder eine Quote in Prozent oder einen Monatsbetrag angeben.");
        }

        if (percent is { } p && (p is <= 0 or > 100 || decimal.Round(p, 2) != p))
        {
            throw new RuleViolationException("Die Quote liegt über 0 % und höchstens bei 100 % (zwei Nachkommastellen).");
        }

        if (monthlyAmount is { } m && (m <= 0 || decimal.Round(m, 2) != m))
        {
            throw new RuleViolationException("Der Monatsbetrag muss über 0 € liegen (zwei Nachkommastellen).");
        }

        if (status == RentReductionStatus.Ended && to is null)
        {
            throw new RuleViolationException("Für eine beendete Minderung bitte den letzten Tag der Minderung angeben.");
        }

        Description = description.Trim();
        From = from;
        To = to;
        Percent = percent;
        MonthlyAmount = monthlyAmount;
        if (status == RentReductionStatus.Acknowledged && AcknowledgedOn is null)
        {
            AcknowledgedOn = today ?? from;
        }

        if (status is RentReductionStatus.Announced or RentReductionStatus.Disputed)
        {
            AcknowledgedOn = null;
        }

        Status = status;
    }

    /// <summary>
    /// Reduction for one service month (positive amount, rounded once to cents). Prorated by day: the reduction applies to the days
    /// it covers within the days the lease runs in the month. A fixed amount is a full monthly amount and never exceeds the rent.
    /// </summary>
    /// <param name="serviceMonth">First day of the month.</param>
    /// <param name="monthlyRent">Gross rent charged for the month (all rent components).</param>
    /// <param name="leaseDaysInMonth">Period within the month in which the lease runs and rent is claimed.</param>
    public decimal AmountFor(DateOnly serviceMonth, decimal monthlyRent, Period leaseDaysInMonth)
    {
        var month = Period.Month(serviceMonth.Year, serviceMonth.Month);
        if (leaseDaysInMonth.Intersect(month) is not { } active || active.Intersect(Period) is not { } covered || monthlyRent <= 0)
        {
            return 0m;
        }

        var amount = Percent is { } percent
            ? monthlyRent * percent / 100m * covered.Days / active.Days
            : (MonthlyAmount ?? 0m) * covered.Days / month.Days;
        return Math.Min(decimal.Round(amount, 2, MidpointRounding.AwayFromZero), monthlyRent);
    }
}
