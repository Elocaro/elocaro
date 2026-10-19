using MietHahn.Domain.Common;
using MietHahn.Domain.Leases;

namespace MietHahn.Domain.Finance;

public enum ChargeType
{
    /// <summary>Monthly rent from the rent components (automatic).</summary>
    Rent = 0,
    AdditionalPayment = 1,

    /// <summary>Tenant credit, e.g. from the utility cost statement (negative amount).</summary>
    Refund = 2,
    DunningFee = 3,
    ReturnedDebitFee = 4,
    DefaultInterest = 5,
    Damages = 6,
    Deposit = 7,

    /// <summary>Outstanding amount from the period before accounting started.</summary>
    BalanceCarriedForward = 8,
    Miscellaneous = 99,
}

/// <summary>Rank for allocation under § 367 BGB.</summary>
public enum AllocationRank
{
    Costs = 0,
    Interest = 1,
    PrincipalClaim = 2,
}

/// <summary>
/// A claim against the tenant (debit) or, for a negative amount, a credit note (credit).
/// The job creates monthly rents per component and service month; everything else is created manually.
/// </summary>
public sealed class Charge : Entity
{
    private Charge()
    {
        Name = "";
    }

    public Charge(Guid leaseId, ChargeType kind, DateOnly due, decimal amount, string name)
    {
        if (kind == ChargeType.Rent)
        {
            throw new ArgumentException("Monatsmieten entstehen über Monatsmiete().", nameof(kind));
        }

        LeaseId = leaseId;
        Kind = kind;
        Due = due;
        Amount = amount;
        Name = name;
        Validate();
    }

    public Guid LeaseId { get; private set; }

    public ChargeType Kind { get; private set; }

    /// <summary>Only for monthly rents: which component.</summary>
    public RentComponentType? Component { get; private set; }

    /// <summary>Only for monthly rents: first day of the month for which the rent is owed.</summary>
    public DateOnly? ServiceMonth { get; private set; }

    public DateOnly Due { get; private set; }

    public decimal Amount { get; private set; }

    public string Name { get; private set; }

    public string? Note { get; set; }

    public bool Automatic => Kind == ChargeType.Rent;

    public AllocationRank Rank => Kind switch
    {
        ChargeType.DunningFee or ChargeType.ReturnedDebitFee => AllocationRank.Costs,
        ChargeType.DefaultInterest => AllocationRank.Interest,
        _ => AllocationRank.PrincipalClaim,
    };

    public static Charge MonthlyRent(Guid leaseId, DateOnly serviceMonth, RentComponentType component, DateOnly due, decimal amount, string name)
    {
        if (serviceMonth.Day != 1)
        {
            throw new ArgumentException("Der Leistungsmonat beginnt am Ersten.", nameof(serviceMonth));
        }

        var charge = new Charge
        {
            LeaseId = leaseId,
            Kind = ChargeType.Rent,
            Component = component,
            ServiceMonth = serviceMonth,
            Due = due,
            Amount = amount,
            Name = name,
        };
        charge.Validate();
        return charge;
    }

    /// <summary>Adjusts an automatic monthly rent to changed lease data.</summary>
    public bool Adjust(DateOnly due, decimal amount, string name)
    {
        if (!Automatic)
        {
            throw new InvalidOperationException("Nur automatische Monatsmieten werden angeglichen.");
        }

        if (Due == due && Amount == amount && Name == name)
        {
            return false;
        }

        Due = due;
        Amount = amount;
        Name = name;
        return true;
    }

    public void Update(ChargeType kind, DateOnly due, decimal amount, string name)
    {
        if (Automatic || kind == ChargeType.Rent)
        {
            throw new RuleViolationException("Monatsmieten ergeben sich aus dem Vertrag; Korrekturen bitte als eigene Sollstellung.");
        }

        Kind = kind;
        Due = due;
        Amount = amount;
        Name = name;
        Validate();
    }

    private void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Name);
        if (Amount == 0)
        {
            throw new RuleViolationException("Der Betrag darf nicht 0 sein.");
        }

        if (decimal.Round(Amount, 2) != Amount)
        {
            throw new RuleViolationException("Beträge haben höchstens zwei Nachkommastellen.");
        }

        if (Kind == ChargeType.Refund && Amount > 0)
        {
            throw new RuleViolationException("Eine Erstattung ist ein Guthaben des Mieters und wird negativ erfasst.");
        }
    }
}

public enum PaymentOrigin
{
    Manual = 0,
    Bank = 1,
}

/// <summary>
/// Payment by the tenant (positive) or payout to the tenant (negative, e.g. a paid-out credit).
/// </summary>
public sealed class Payment : Entity
{
    private Payment()
    {
    }

    public Payment(Guid leaseId, DateOnly date, decimal amount, PaymentOrigin origin = PaymentOrigin.Manual)
    {
        LeaseId = leaseId;
        Date = date;
        Amount = amount;
        Origin = origin;
        Validate();
    }

    public Guid LeaseId { get; private set; }

    /// <summary>Day the payment was received (value date), decisive for the cash-basis principle.</summary>
    public DateOnly Date { get; private set; }

    public decimal Amount { get; private set; }

    public PaymentOrigin Origin { get; private set; }

    public string? Purpose { get; set; }

    /// <summary>Tenant's designation of payment (§ 366 Abs. 1 BGB): settle this claim first.</summary>
    public Guid? ChargeId { get; set; }

    public void Update(DateOnly date, decimal amount)
    {
        Date = date;
        Amount = amount;
        Validate();
    }

    private void Validate()
    {
        if (Amount == 0 || decimal.Round(Amount, 2) != Amount)
        {
            throw new RuleViolationException("Bitte einen Betrag ungleich 0 mit höchstens zwei Nachkommastellen angeben.");
        }
    }
}

public enum AllocationOrder
{
    /// <summary>§§ 366, 367 BGB: costs → interest → oldest claim.</summary>
    Statutory = 0,

    /// <summary>Only after due date; costs and interest take no precedence.</summary>
    OldestFirst = 1,

    /// <summary>Monthly rents first (oldest first), then all other claims.</summary>
    RentFirst = 2,
}

/// <summary>Accounting settings (exactly one record).</summary>
public sealed class FinanceSettings : Entity
{
    /// <summary>From this month on the job creates monthly rents; empty = no automatic charges yet.</summary>
    public DateOnly? AccountingStart { get; private set; }

    public AllocationOrder Allocation { get; set; }

    public void SetAccountingStart(DateOnly? from) =>
        AccountingStart = from is { } day ? new DateOnly(day.Year, day.Month, 1) : null;
}
