using System.ComponentModel.DataAnnotations;
using Elocaro.Application.Common;
using Elocaro.Application.Receipts;
using Elocaro.Domain.Bank;
using Elocaro.Domain.Bank.Matching;

namespace Elocaro.Application.Bank;

/// <summary>
/// Assignment of bank transactions: income to the tenant account (payment, optionally on specific charges), expenses to
/// cost items of a receipt. Suggestions come from user rules and the matching engine; every assignment can be undone.
/// </summary>
public interface IPaymentAssignment
{
    /// <summary>Open transactions with the best suggestion each (newest first).</summary>
    Task<IReadOnlyList<InboxRow>> InboxAsync(Guid? bankAccountId = null, CancellationToken cancellationToken = default);

    /// <summary>The transaction with ranked suggestions; <c>null</c> if it does not exist.</summary>
    Task<AssignmentProposal?> ProposalAsync(Guid transactionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Assigns the whole transaction in one step. The parts must add up exactly to the amount; one part may stay empty and
    /// receives the remainder. Nothing is changed if a part is invalid.
    /// </summary>
    Task<Result> AssignAsync(Guid transactionId, IReadOnlyList<AssignmentPartInput> parts, CancellationToken cancellationToken = default);

    /// <summary>Applies the suggestion with the given rank (0 = best); an "ignore" rule ignores the transaction.</summary>
    Task<Result> AcceptAsync(Guid transactionId, int rank = 0, CancellationToken cancellationToken = default);

    /// <summary>Reverts payments, cost items and receipts created by the assignment; the transaction is open again.</summary>
    Task<Result> UnassignAsync(Guid transactionId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AssignmentView>> AssignmentsAsync(Guid transactionId, CancellationToken cancellationToken = default);

    Task<AssignmentOptions> OptionsAsync(CancellationToken cancellationToken = default);
}

/// <summary>Rules for suggestions (administration).</summary>
public interface IMatchRules
{
    Task<IReadOnlyList<MatchRuleRow>> ListAsync(CancellationToken cancellationToken = default);

    Task<MatchRuleInput?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result> SaveAsync(Guid? id, MatchRuleInput data, CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

/// <summary>One part of an assignment: a payment of a lease (income) or a cost item (expense).</summary>
public sealed class AssignmentPartInput
{
    /// <summary>Positive amount; empty = remainder of the transaction (at most one part).</summary>
    public decimal? Amount { get; set; }

    /// <summary>Income: lease; the payment is allocated according to the allocation order of the settings.</summary>
    public Guid? LeaseId { get; set; }

    /// <summary>Income: settle this charge first (designation of payment); the lease follows from the charge.</summary>
    public Guid? ChargeId { get; set; }

    /// <summary>Expense: key of the cost type.</summary>
    public string? CostType { get; set; }

    public Guid? PropertyId { get; set; }

    public Guid? UnitId { get; set; }

    /// <summary>Empty = service period of the receipt or the calendar year of the booking.</summary>
    public DateOnly? From { get; set; }

    public DateOnly? To { get; set; }

    /// <summary>Empty = according to the cost type.</summary>
    public bool? Allocable { get; set; }

    /// <summary>Existing receipt to add the cost item to; empty = a receipt is created for the transaction.</summary>
    public Guid? ReceiptId { get; set; }

    /// <summary>Instalment of this loan (outflows only); the interest share is taken from the repayment plan unless <see cref="LoanInterest"/> is given.</summary>
    public Guid? LoanId { get; set; }

    public decimal? LoanInterest { get; set; }

    [StringLength(500)]
    public string? Note { get; set; }

    public bool IsLoan => LoanId is not null;

    public bool IsCost => !string.IsNullOrWhiteSpace(CostType) || PropertyId is not null || ReceiptId is not null;

    public bool IsEmpty => Amount is null && LeaseId is null && ChargeId is null && !IsCost && !IsLoan && LoanInterest is null && From is null && To is null && UnitId is null && Allocable is null && string.IsNullOrWhiteSpace(Note);
}

public sealed record InboxRow(TransactionRow Transaction, SuggestionView? Suggestion, int Alternatives);

public sealed record SuggestionView(
    SuggestionKind Kind,
    int Score,
    string Confidence,
    IReadOnlyList<string> Reasons,
    string Target,
    IReadOnlyList<SuggestionPartView> Parts);

public sealed record SuggestionPartView(string Text, decimal Amount);

public sealed record AssignmentProposal(TransactionRow Transaction, string Account, IReadOnlyList<SuggestionView> Suggestions);

/// <param name="Target">Lease text or cost type with property.</param>
/// <param name="Link">Page of the created record (tenant account or receipt).</param>
public sealed record AssignmentView(Guid Id, decimal Amount, string Kind, string Target, string? Detail, string? Link, bool ReceiptWithoutFile);

public sealed record AssignmentOptions(
    IReadOnlyList<LeaseOption> Leases,
    IReadOnlyList<ChargeOption> Charges,
    IReadOnlyList<ReferenceOption> Properties,
    IReadOnlyList<ReferenceOption> Units,
    IReadOnlyList<ReceiptOption> Receipts)
{
    /// <summary>Loans an outflow can be booked to as an instalment.</summary>
    public IReadOnlyList<LoanOption> Loans { get; init; } = [];
}

public sealed record LeaseOption(Guid Id, string Text);

public sealed record LoanOption(Guid Id, string Text);

public sealed record ChargeOption(Guid Id, Guid LeaseId, string Text, decimal Open);

public sealed record ReceiptOption(Guid Id, string Text, decimal Open);

public sealed class MatchRuleInput
{
    [Required(ErrorMessage = "Bitte einen Namen angeben.")]
    [StringLength(100)]
    public string Name { get; set; } = "";

    [Range(0, 10000)]
    public int Priority { get; set; } = 100;

    public RuleDirection Direction { get; set; }

    [StringLength(42)]
    public string? CounterpartyIban { get; set; }

    [StringLength(200)]
    public string? CounterpartyContains { get; set; }

    [StringLength(200)]
    public string? PurposeContains { get; set; }

    public RuleAction Action { get; set; } = RuleAction.Expense;

    public string? CostType { get; set; }

    public Guid? PropertyId { get; set; }

    public Guid? UnitId { get; set; }

    public bool? Allocable { get; set; }

    public Guid? LeaseId { get; set; }
}

public sealed record MatchRuleRow(Guid Id, string Name, int Priority, string Condition, string Target, RuleAction Action);
