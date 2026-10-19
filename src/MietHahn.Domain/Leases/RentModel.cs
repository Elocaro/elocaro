using MietHahn.Domain.Common;

namespace MietHahn.Domain.Leases;

public enum RentModel
{
    /// <summary>Erhöhung auf die ortsübliche Vergleichsmiete (§ 558 BGB).</summary>
    Standard = 0,

    /// <summary>Staffelmiete (§ 557a BGB): vereinbarte Erhöhungen zu festen Terminen.</summary>
    Graduated = 1,

    /// <summary>Indexmiete (§ 557b BGB): Anpassung an den Verbraucherpreisindex.</summary>
    Index = 2,
}

/// <summary>Eine vereinbarte Staffel: ab diesem Tag gilt die neue Kaltmiete.</summary>
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

    /// <summary>Neue Kaltmiete in Euro (§ 557a: als Geldbetrag ausgewiesen).</summary>
    public decimal BaseRent { get; private set; }

    /// <summary>Wann die Staffel als Mietbestandteil übernommen wurde.</summary>
    public DateTimeOffset? AppliedAt { get; private set; }

    internal void MarkApplied(DateTimeOffset timestamp) => AppliedAt = timestamp;
}

/// <summary>Monatlicher Verbraucherpreisindex (Destatis, Basis 2020 = 100, Tabelle 61111-0002).</summary>
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

    /// <summary>Erster Tag des Monats.</summary>
    public DateOnly Month { get; private set; }

    /// <summary>Indexstand mit einer Nachkommastelle, z. B. 121,7.</summary>
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

/// <summary>Rechenregeln der Indexmiete (§ 557b BGB).</summary>
public static class IndexedRent
{
    /// <summary>Die Miete muss jeweils mindestens ein Jahr unverändert bleiben (§ 557b Abs. 2 BGB).</summary>
    public const int LockoutPeriodMonths = 12;

    /// <summary>Neue Kaltmiete = bisherige × (VPI neu ÷ VPI alt), kaufmännisch auf Cent gerundet.</summary>
    public static decimal NewRent(decimal previousRent, decimal oldCpi, decimal newCpi)
    {
        if (oldCpi <= 0 || newCpi <= 0)
        {
            throw new RuleViolationException("Indexwerte müssen größer als 0 sein.");
        }

        return Math.Round(previousRent * newCpi / oldCpi, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>Veränderung in Prozent mit einer Nachkommastelle (z. B. für das Anpassungsschreiben).</summary>
    public static decimal ChangePercent(decimal oldCpi, decimal newCpi) =>
        Math.Round(((newCpi / oldCpi) - 1) * 100, 1, MidpointRounding.AwayFromZero);

    /// <summary>Frühester Tag, ab dem die angepasste Miete gelten darf (ein Jahr nach der letzten Änderung).</summary>
    public static DateOnly EarliestEffectiveDate(DateOnly lastChange) => lastChange.AddMonths(LockoutPeriodMonths);

    /// <summary>Die geänderte Miete gilt ab dem übernächsten Monat nach Zugang der Erklärung (§ 557b Abs. 3 BGB).</summary>
    public static DateOnly EffectiveFrom(DateOnly declarationReceived) =>
        new DateOnly(declarationReceived.Year, declarationReceived.Month, 1).AddMonths(2);
}
