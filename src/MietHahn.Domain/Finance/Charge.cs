using MietHahn.Domain.Common;
using MietHahn.Domain.Leases;

namespace MietHahn.Domain.Finance;

public enum ChargeType
{
    /// <summary>Monatliche Miete aus den Mietbestandteilen (automatisch).</summary>
    Rent = 0,
    AdditionalPayment = 1,

    /// <summary>Guthaben des Mieters, z. B. aus der Nebenkostenabrechnung (negativer Betrag).</summary>
    Refund = 2,
    DunningFee = 3,
    ReturnedDebitFee = 4,
    DefaultInterest = 5,
    Damages = 6,
    Deposit = 7,

    /// <summary>Offener Betrag aus der Zeit vor dem Buchhaltungsbeginn.</summary>
    BalanceCarriedForward = 8,
    Miscellaneous = 99,
}

/// <summary>Rang bei der Verrechnung nach § 367 BGB.</summary>
public enum AllocationRank
{
    Costs = 0,
    Interest = 1,
    PrincipalClaim = 2,
}

/// <summary>
/// Eine Forderung an den Mieter (Soll) bzw. bei negativem Betrag eine Gutschrift (Haben).
/// Monatsmieten erzeugt der Job je Bestandteil und Leistungsmonat; alles andere wird von Hand angelegt.
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

    /// <summary>Nur bei Monatsmieten: welcher Bestandteil.</summary>
    public RentComponentType? Component { get; private set; }

    /// <summary>Nur bei Monatsmieten: erster Tag des Monats, für den die Miete geschuldet ist.</summary>
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

    /// <summary>Gleicht eine automatische Monatsmiete an geänderte Vertragsdaten an.</summary>
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
/// Zahlung des Mieters (positiv) bzw. Auszahlung an den Mieter (negativ, z. B. ausgezahltes Guthaben).
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

    /// <summary>Tag des Zahlungseingangs (Wertstellung), maßgeblich für das Zuflussprinzip.</summary>
    public DateOnly Date { get; private set; }

    public decimal Amount { get; private set; }

    public PaymentOrigin Origin { get; private set; }

    public string? Purpose { get; set; }

    /// <summary>Tilgungsbestimmung des Mieters (§ 366 Abs. 1 BGB): diese Forderung zuerst.</summary>
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
    /// <summary>§§ 366, 367 BGB: Kosten → Zinsen → älteste Forderung.</summary>
    Statutory = 0,

    /// <summary>Nur nach Fälligkeit, Kosten und Zinsen ohne Vorrang.</summary>
    OldestFirst = 1,

    /// <summary>Erst Monatsmieten (älteste zuerst), dann alle übrigen Forderungen.</summary>
    RentFirst = 2,
}

/// <summary>Einstellungen der Buchhaltung (genau ein Datensatz).</summary>
public sealed class FinanceSettings : Entity
{
    /// <summary>Ab diesem Monat erzeugt der Job Monatsmieten; leer = noch keine automatischen Sollstellungen.</summary>
    public DateOnly? AccountingStart { get; private set; }

    public AllocationOrder Allocation { get; set; }

    public void SetAccountingStart(DateOnly? from) =>
        AccountingStart = from is { } day ? new DateOnly(day.Year, day.Month, 1) : null;
}
