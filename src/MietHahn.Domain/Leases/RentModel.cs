using MietHahn.Domain.Common;

namespace MietHahn.Domain.Leases;

public enum RentModel
{
    /// <summary>Increase to the local comparative rent (§ 558 BGB).</summary>
    Standard = 0,

    /// <summary>Graduated rent (§ 557a BGB): agreed increases on fixed dates.</summary>
    Graduated = 1,

    /// <summary>Index rent (§ 557b BGB): adjustment to the consumer price index.</summary>
    Index = 2,
}

/// <summary>An agreed step: from this day the new base rent applies.</summary>
public sealed class RentStep : Entity
{
    private RentStep()
    {
    }

    internal RentStep(Guid leaseId, DateOnly from, decimal baseRent)
    {
        LeaseId = leaseId;
        From = from;
        BaseRent = baseRent;
    }

    public Guid LeaseId { get; private set; }

    public DateOnly From { get; private set; }

    /// <summary>New base rent in euros (§ 557a: stated as a monetary amount).</summary>
    public decimal BaseRent { get; private set; }

    /// <summary>When the step was adopted as a rent component.</summary>
    public DateTimeOffset? AppliedAt { get; private set; }

    internal void MarkApplied(DateTimeOffset timestamp) => AppliedAt = timestamp;

    internal void Correct(DateOnly from, decimal baseRent)
    {
        From = from;
        BaseRent = baseRent;
    }
}

/// <summary>Monthly consumer price index (Destatis, base 2020 = 100, table 61111-0002).</summary>
public sealed class CpiValue : Entity
{
    private CpiValue()
    {
        Source = "";
    }

    public CpiValue(DateOnly month, decimal value, string source = "manuell")
    {
        if (value is <= 0 or > 1000)
        {
            throw new RuleViolationException("Der Indexwert ist unplausibel.");
        }

        Month = new DateOnly(month.Year, month.Month, 1);
        Value = decimal.Round(value, 1);
        Source = source;
    }

    /// <summary>First day of the month.</summary>
    public DateOnly Month { get; private set; }

    /// <summary>Index value with one decimal place, e.g. 121.7.</summary>
    public decimal Value { get; private set; }

    public string Source { get; private set; }

    public void Update(decimal value, string source)
    {
        if (value is <= 0 or > 1000)
        {
            throw new RuleViolationException("Der Indexwert ist unplausibel.");
        }

        Value = decimal.Round(value, 1);
        Source = source;
    }
}

/// <summary>Calculation rules of the index rent (§ 557b BGB).</summary>
public static class IndexedRent
{
    /// <summary>The rent must remain unchanged for at least one year each time (§ 557b Abs. 2 BGB).</summary>
    public const int LockoutPeriodMonths = 12;

    /// <summary>New base rent = previous × (new CPI ÷ old CPI), commercially rounded to cents.</summary>
    public static decimal NewRent(decimal previousRent, decimal oldCpi, decimal newCpi)
    {
        if (oldCpi <= 0 || newCpi <= 0)
        {
            throw new RuleViolationException("Indexwerte müssen größer als 0 sein.");
        }

        return Math.Round(previousRent * newCpi / oldCpi, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>Change in percent with one decimal place (e.g. for the adjustment letter).</summary>
    public static decimal ChangePercent(decimal oldCpi, decimal newCpi) =>
        Math.Round(((newCpi / oldCpi) - 1) * 100, 1, MidpointRounding.AwayFromZero);

    /// <summary>Earliest day from which the adjusted rent may apply (one year after the last change).</summary>
    public static DateOnly EarliestEffectiveDate(DateOnly lastChange) => lastChange.AddMonths(LockoutPeriodMonths);

    /// <summary>The changed rent applies from the month after next following receipt of the declaration (§ 557b Abs. 3 BGB).</summary>
    public static DateOnly EffectiveFrom(DateOnly declarationReceived) =>
        new DateOnly(declarationReceived.Year, declarationReceived.Month, 1).AddMonths(2);
}
