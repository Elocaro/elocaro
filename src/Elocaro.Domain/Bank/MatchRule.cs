using Elocaro.Domain.Bank.Matching;
using Elocaro.Domain.Common;
using Elocaro.Domain.Receipts;

namespace Elocaro.Domain.Bank;

public enum RuleDirection
{
    Any = 0,
    Income = 1,
    Expense = 2,
}

public enum RuleAction
{
    /// <summary>Private or irrelevant transactions disappear from the inbox.</summary>
    Ignore = 0,

    /// <summary>Expense: cost type and property.</summary>
    Expense = 1,

    /// <summary>Income: payment of a specific lease.</summary>
    Lease = 2,
}

/// <summary>
/// User-defined assignment rule, e.g. "counter IBAN X → cost type water, property Y". All set conditions must apply;
/// rules are checked in order of <see cref="Priority"/> before the heuristic scoring.
/// </summary>
public sealed class MatchRule : Entity
{
    private MatchRule()
    {
        Name = "";
    }

    public MatchRule(string name, RuleAction action)
    {
        Name = name;
        Action = action;
    }

    public string Name { get; set; }

    /// <summary>Lower value = checked first.</summary>
    public int Priority { get; set; } = 100;

    public RuleDirection Direction { get; set; }

    /// <summary>Compact without spaces, in uppercase.</summary>
    [Confidential]
    public string? CounterpartyIban { get; private set; }

    /// <summary>Words of the counter name; several alternatives separated by semicolon ("Stadtwerke; SWK").</summary>
    public string? CounterpartyContains { get; set; }

    /// <summary>Words of the purpose; several alternatives separated by semicolon.</summary>
    public string? PurposeContains { get; set; }

    public RuleAction Action { get; set; }

    /// <summary>Key from <see cref="CostTypes"/> (action <see cref="RuleAction.Expense"/>).</summary>
    public string? CostType { get; set; }

    public Guid? PropertyId { get; set; }

    public Guid? UnitId { get; set; }

    /// <summary>Empty = according to the cost type.</summary>
    public bool? Allocable { get; set; }

    /// <summary>Action <see cref="RuleAction.Lease"/>.</summary>
    public Guid? LeaseId { get; set; }

    public void SetCounterpartyIban(string? iban)
    {
        if (string.IsNullOrWhiteSpace(iban))
        {
            CounterpartyIban = null;
            return;
        }

        if (!Iban.IsValid(iban))
        {
            throw new RuleViolationException("Die IBAN ist ungültig (Prüfsumme).");
        }

        CounterpartyIban = Iban.Compact(iban);
    }

    public bool Matches(MatchInput transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        if ((Direction == RuleDirection.Income && transaction.Amount <= 0) || (Direction == RuleDirection.Expense && transaction.Amount >= 0))
        {
            return false;
        }

        if (CounterpartyIban is { } iban && (transaction.CounterpartyIban is null || Iban.Compact(transaction.CounterpartyIban) != iban))
        {
            return false;
        }

        return AnyPhrase(CounterpartyContains, transaction.CounterpartyName) && AnyPhrase(PurposeContains, transaction.Purpose);
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new RuleViolationException("Bitte einen Namen für die Regel angeben.");
        }

        if (CounterpartyIban is null && string.IsNullOrWhiteSpace(CounterpartyContains) && string.IsNullOrWhiteSpace(PurposeContains))
        {
            throw new RuleViolationException("Bitte mindestens eine Bedingung angeben (Gegen-IBAN, Name der Gegenseite oder Verwendungszweck).");
        }

        switch (Action)
        {
            case RuleAction.Expense:
                if (CostType is null || !CostTypes.IsKnown(CostType))
                {
                    throw new RuleViolationException("Bitte die Kostenart wählen.");
                }

                if (Direction == RuleDirection.Income)
                {
                    throw new RuleViolationException("Eine Ausgaben-Regel passt nicht zur Richtung „Eingänge“.");
                }

                LeaseId = null;
                break;
            case RuleAction.Lease:
                if (LeaseId is null)
                {
                    throw new RuleViolationException("Bitte das Mietverhältnis wählen.");
                }

                if (Direction == RuleDirection.Expense)
                {
                    throw new RuleViolationException("Eine Mieter-Regel passt nicht zur Richtung „Ausgaben“.");
                }

                CostType = null;
                PropertyId = null;
                UnitId = null;
                Allocable = null;
                break;
            default:
                CostType = null;
                PropertyId = null;
                UnitId = null;
                Allocable = null;
                LeaseId = null;
                break;
        }
    }

    private static bool AnyPhrase(string? alternatives, string? text)
    {
        if (string.IsNullOrWhiteSpace(alternatives))
        {
            return true;
        }

        return alternatives.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(phrase => TextMatching.ContainsPhrase(text, phrase));
    }
}
