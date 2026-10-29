using Elocaro.Domain.Common;

namespace Elocaro.Domain.Tax;

/// <summary>Loan that finances a property; the interest is deductible as income-related expenses (Werbungskosten).</summary>
public sealed class Loan : Entity
{
    private Loan()
    {
        Bank = "";
    }

    public Loan(Guid propertyId, string bank, string? loanNumber)
    {
        PropertyId = propertyId;
        Bank = "";
        SetBank(bank, loanNumber);
    }

    public Guid PropertyId { get; private set; }

    public string Bank { get; private set; }

    public string? LoanNumber { get; private set; }

    public decimal Principal { get; private set; }

    public DateOnly DisbursementDate { get; private set; }

    public DateOnly FirstPaymentDate { get; private set; }

    public decimal InterestRatePercent { get; private set; }

    public decimal Instalment { get; private set; }

    /// <summary>1 = monthly, 3 = quarterly.</summary>
    public int MonthsPerPeriod { get; private set; } = 1;

    /// <summary>End of the fixed-interest period (reminder).</summary>
    public DateOnly? FixedUntil { get; private set; }

    /// <summary>Share of the loan that finances the rented property in percent (rest: private use).</summary>
    public decimal SharePercent { get; private set; } = 100m;

    public string? Notes { get; private set; }

    public void SetBank(string bank, string? loanNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bank);
        Bank = bank.Trim();
        LoanNumber = string.IsNullOrWhiteSpace(loanNumber) ? null : loanNumber.Trim();
    }

    public void SetTerms(
        decimal principal,
        DateOnly disbursementDate,
        DateOnly firstPaymentDate,
        decimal interestRatePercent,
        decimal instalment,
        int monthsPerPeriod,
        DateOnly? fixedUntil,
        decimal sharePercent,
        string? notes)
    {
        if (principal <= 0)
        {
            throw new RuleViolationException("Der Darlehensbetrag muss größer als 0 sein.");
        }

        if (interestRatePercent is < 0 or > 30)
        {
            throw new RuleViolationException("Der Sollzins muss zwischen 0 und 30 % liegen.");
        }

        if (instalment <= 0)
        {
            throw new RuleViolationException("Die Rate muss größer als 0 sein.");
        }

        if (monthsPerPeriod is not (1 or 3))
        {
            throw new RuleViolationException("Die Ratenfolge ist monatlich oder vierteljährlich.");
        }

        if (firstPaymentDate <= disbursementDate)
        {
            throw new RuleViolationException("Die erste Rate muss nach der Auszahlung fällig sein.");
        }

        if (sharePercent is <= 0 or > 100)
        {
            throw new RuleViolationException("Der Anteil muss zwischen 0 und 100 % liegen.");
        }

        Principal = principal;
        DisbursementDate = disbursementDate;
        FirstPaymentDate = firstPaymentDate;
        InterestRatePercent = interestRatePercent;
        Instalment = instalment;
        MonthsPerPeriod = monthsPerPeriod;
        FixedUntil = fixedUntil;
        SharePercent = sharePercent;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }

    public LoanTerms ToCalculation() => new(Principal, DisbursementDate, FirstPaymentDate, InterestRatePercent, Instalment, MonthsPerPeriod);
}

/// <summary>Extra repayment of a loan.</summary>
public sealed class LoanExtraPayment : Entity
{
    private LoanExtraPayment()
    {
    }

    public LoanExtraPayment(Guid loanId, DateOnly date, decimal amount)
    {
        if (amount <= 0)
        {
            throw new RuleViolationException("Die Sondertilgung muss größer als 0 sein.");
        }

        LoanId = loanId;
        Date = date;
        Amount = amount;
    }

    public Guid LoanId { get; private set; }

    public DateOnly Date { get; private set; }

    public decimal Amount { get; private set; }

    public void Change(DateOnly date, decimal amount)
    {
        if (amount <= 0)
        {
            throw new RuleViolationException("Die Sondertilgung muss größer als 0 sein.");
        }

        Date = date;
        Amount = amount;
    }

    public ExtraRepayment ToCalculation() => new(Date, Amount);
}

/// <summary>Annual interest certificate of the bank; takes precedence over the calculated plan.</summary>
public sealed class LoanCertificate : Entity
{
    private LoanCertificate()
    {
    }

    public LoanCertificate(Guid loanId, int year, decimal interest, decimal? closingBalance)
    {
        LoanId = loanId;
        Set(year, interest, closingBalance);
    }

    public Guid LoanId { get; private set; }

    public int Year { get; private set; }

    public decimal Interest { get; private set; }

    public decimal? ClosingBalance { get; private set; }

    public void Set(int year, decimal interest, decimal? closingBalance)
    {
        if (year is < 1950 or > 2100)
        {
            throw new RuleViolationException("Bitte ein gültiges Jahr angeben.");
        }

        if (interest < 0 || closingBalance < 0)
        {
            throw new RuleViolationException("Beträge dürfen nicht negativ sein.");
        }

        Year = year;
        Interest = interest;
        ClosingBalance = closingBalance;
    }
}

public enum FinancingCostKind
{
    Disagio,
    CommitmentInterest,
    LandCharge,
    Other,
}

/// <summary>One-off financing cost of a property (income-related expenses in the year it is paid).</summary>
public sealed class FinancingCost : Entity
{
    private FinancingCost()
    {
        Description = "";
    }

    public FinancingCost(Guid propertyId, Guid? loanId, DateOnly date, decimal amount, FinancingCostKind kind, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        if (amount <= 0)
        {
            throw new RuleViolationException("Der Betrag muss größer als 0 sein.");
        }

        PropertyId = propertyId;
        LoanId = loanId;
        Date = date;
        Amount = amount;
        Kind = kind;
        Description = description.Trim();
    }

    public Guid PropertyId { get; private set; }

    public Guid? LoanId { get; private set; }

    public DateOnly Date { get; private set; }

    public decimal Amount { get; private set; }

    public FinancingCostKind Kind { get; private set; }

    public string Description { get; private set; }

    public void Change(DateOnly date, decimal amount, FinancingCostKind kind, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        if (amount <= 0)
        {
            throw new RuleViolationException("Der Betrag muss größer als 0 sein.");
        }

        Date = date;
        Amount = amount;
        Kind = kind;
        Description = description.Trim();
    }
}
