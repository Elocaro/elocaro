using System.ComponentModel.DataAnnotations;
using Elocaro.Application.Common;
using Elocaro.Domain.Leases;
using Elocaro.Domain.Statements;

namespace Elocaro.Application.Statements;

/// <summary>
/// Letter and delivery of a released statement: PDF per tenancy, booking of the additional payment or credit, dispatch by email or
/// post with proof of receipt and the proposal to adjust the prepayments (§ 560 para. 4 BGB).
/// </summary>
public interface IStatementDispatch
{
    /// <summary>Tenancies of the statement with their balance, dispatch and prepayment proposals; <c>null</c> if the statement does not exist.</summary>
    Task<DispatchOverview?> OverviewAsync(Guid statementId, CancellationToken cancellationToken = default);

    /// <summary>Renders the letter without storing, booking or sending anything.</summary>
    Task<byte[]> PreviewAsync(Guid statementId, Guid leaseId, DispatchInput options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates and stores the letter (file store, document library; shared with the tenant in the portal), books the balance
    /// (for a correction only the difference to earlier dispatches) and sends the email if chosen.
    /// </summary>
    Task<Result> DispatchAsync(Guid statementId, Guid leaseId, DispatchInput options, CancellationToken cancellationToken = default);

    /// <summary>Records the proof of receipt (e.g. delivery date of a registered letter).</summary>
    Task<Result> ConfirmReceiptAsync(Guid dispatchId, DeliveryConfirmation input, CancellationToken cancellationToken = default);

    /// <summary>Applies a proposal: from its effective date the prepayment is a new rent component.</summary>
    Task<Result> ApplyProposalAsync(Guid proposalId, decimal? amount, CancellationToken cancellationToken = default);

    Task<Result> DeclineProposalAsync(Guid proposalId, CancellationToken cancellationToken = default);
}

public sealed class DispatchInput
{
    public DispatchChannel Channel { get; set; } = DispatchChannel.Email;

    /// <summary>Whether the letter contains a proposal for new prepayments.</summary>
    public bool ProposePrepayments { get; set; } = true;

    /// <summary>Safety margin on the prepayment proposal; <c>null</c> = configured default.</summary>
    [Range(0, 100, ErrorMessage = "Der Sicherheitszuschlag muss zwischen 0 und 100 % liegen.")]
    public decimal? SafetyPercent { get; set; }

    /// <summary>Effective date of the new prepayments; <c>null</c> = first day of the month after the next.</summary>
    public DateOnly? PrepaymentFrom { get; set; }
}

public sealed class DeliveryConfirmation
{
    [Required(ErrorMessage = "Bitte das Zugangsdatum angeben.")]
    public DateOnly? ReceivedOn { get; set; }

    [StringLength(500)]
    public string? Note { get; set; }
}

public sealed record DispatchOverview(
    StoredStatement Statement,
    IReadOnlyList<TenancyDispatch> Tenancies,
    DateOnly Deadline,
    bool LandlordProfileMissing);

/// <param name="Dispatch">The dispatch of this statement revision; <c>null</c> if not sent yet.</param>
/// <param name="EarlierBooked">Amount booked with earlier revisions of the statement.</param>
/// <param name="ToBook">Amount that a dispatch would book now (balance minus earlier booked, 0 if not claimable after the deadline).</param>
/// <param name="Emails">Email addresses of the tenants (empty = postal dispatch only).</param>
public sealed record TenancyDispatch(
    Guid LeaseId,
    string Label,
    string Rental,
    decimal Balance,
    decimal EarlierBooked,
    decimal ToBook,
    bool ClaimExcluded,
    IReadOnlyList<string> Emails,
    DispatchRecord? Dispatch);

public sealed record DispatchRecord(
    Guid Id,
    DispatchChannel Channel,
    DateOnly SentOn,
    Guid FileId,
    decimal BookedAmount,
    string? Note,
    string? MailStatus,
    DateTimeOffset? MailSentAt,
    DateOnly? ReceivedOn,
    string? ReceiptNote,
    IReadOnlyList<ProposalRow> Proposals);

public sealed record ProposalRow(Guid Id, RentComponentType Component, decimal Current, decimal Proposed, DateOnly EffectiveFrom, ProposalStatus Status, DateOnly? DecidedOn);
