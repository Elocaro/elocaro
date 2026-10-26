using MietHahn.Domain.Common;

namespace MietHahn.Domain.Deposits;

public enum DepositEntryKind
{
    /// <summary>The tenant paid (part of) the deposit.</summary>
    Received = 0,

    /// <summary>Interest credited to the deposit (§ 551 Abs. 3 BGB: the interest belongs to the tenant and increases the deposit).</summary>
    Interest = 1,

    /// <summary>Paid back to the tenant.</summary>
    Refund = 2,

    /// <summary>Set off against a claim of the landlord (damage, arrears, statement); the note says which.</summary>
    Deduction = 3,
}

/// <summary>One movement of the deposit of a lease; the balance is the sum of the movements (see <see cref="DepositAccount"/>).</summary>
public sealed class DepositEntry : Entity
{
    private DepositEntry()
    {
    }

    public DepositEntry(Guid leaseId, DepositEntryKind kind, DateOnly date, decimal amount, string? note = null)
    {
        if (amount <= 0 || decimal.Round(amount, 2) != amount)
        {
            throw new RuleViolationException("Bitte einen Betrag über 0 € mit höchstens zwei Nachkommastellen angeben.");
        }

        if (kind == DepositEntryKind.Deduction && string.IsNullOrWhiteSpace(note))
        {
            throw new RuleViolationException("Bei einer Verrechnung bitte angeben, wogegen verrechnet wird.");
        }

        LeaseId = leaseId;
        Kind = kind;
        Date = date;
        Amount = amount;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }

    public Guid LeaseId { get; private set; }

    public DepositEntryKind Kind { get; private set; }

    public DateOnly Date { get; private set; }

    /// <summary>Always positive; the kind says whether it adds to or reduces the deposit.</summary>
    public decimal Amount { get; private set; }

    public string? Note { get; private set; }

    /// <summary>Corrects the entry (typing errors) with the same rules as when creating it.</summary>
    public void Correct(DepositEntryKind kind, DateOnly date, decimal amount, string? note)
    {
        var check = new DepositEntry(LeaseId, kind, date, amount, note);
        Kind = check.Kind;
        Date = check.Date;
        Amount = check.Amount;
        Note = check.Note;
    }

    /// <summary>The effect on the balance: positive for received and interest, negative for refund and deduction.</summary>
    public decimal Effect => Kind is DepositEntryKind.Received or DepositEntryKind.Interest ? Amount : -Amount;
}

/// <summary>The deposit of one lease: what was agreed, what came in and what is left.</summary>
public sealed record DepositAccount(decimal? Agreed, decimal Received, decimal Interest, decimal Refunded, decimal Deducted)
{
    public decimal Balance => Received + Interest - Refunded - Deducted;

    /// <summary>Part of the agreed deposit that has not been paid yet (never negative).</summary>
    public decimal Outstanding => Math.Max(0, (Agreed ?? 0) - Received);

    public static DepositAccount Of(decimal? agreed, IEnumerable<DepositEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var list = entries.ToList();
        decimal Sum(DepositEntryKind kind) => list.Where(e => e.Kind == kind).Sum(e => e.Amount);
        return new DepositAccount(agreed, Sum(DepositEntryKind.Received), Sum(DepositEntryKind.Interest), Sum(DepositEntryKind.Refund), Sum(DepositEntryKind.Deduction));
    }
}
