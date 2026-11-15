using System.ComponentModel.DataAnnotations;
using Elocaro.Application.Common;
using Elocaro.Domain.Payments;

namespace Elocaro.Application.Payments;

/// <summary>SEPA core direct debit mandates of the tenants (who allowed which account, when, with which reference).</summary>
public interface IDirectDebitMandates
{
    /// <summary>All mandates, the valid ones first.</summary>
    Task<IReadOnlyList<MandateRow>> ListAsync(CancellationToken cancellationToken = default);

    Task<MandateInput?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Leases a mandate can be created for: the running ones and the ones that start later.</summary>
    Task<IReadOnlyList<MandateLeaseOption>> LeaseOptionsAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates a mandate. An empty reference is generated; a given one has to be unique. Sets the payment method of the lease to direct debit.</summary>
    Task<Result> CreateAsync(Guid leaseId, MandateInput input, CancellationToken cancellationToken = default);

    Task<Result> UpdateAsync(Guid id, MandateInput input, CancellationToken cancellationToken = default);

    /// <summary>The tenant revoked the mandate or the lease ended: no more collections from this day on.</summary>
    Task<Result> RevokeAsync(Guid id, DateOnly day, CancellationToken cancellationToken = default);

    Task<Result> ReactivateAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed class MandateInput
{
    /// <summary>Empty = generated (<c>MND-20260105-1F3A9C2B</c>). Cannot be changed later.</summary>
    [StringLength(DirectDebitMandate.MaxReference)]
    public string? Reference { get; set; }

    [Required(ErrorMessage = "Bitte den Namen des Kontoinhabers angeben.")]
    [StringLength(DirectDebitMandate.MaxName, ErrorMessage = "Der Name darf höchstens 70 Zeichen lang sein.")]
    public string DebtorName { get; set; } = "";

    [Required(ErrorMessage = "Bitte die IBAN angeben.")]
    [Iban]
    public string DebtorIban { get; set; } = "";

    [StringLength(11)]
    public string? DebtorBic { get; set; }

    [Required(ErrorMessage = "Bitte das Datum der Unterschrift angeben.")]
    public DateOnly? SignedOn { get; set; }

    public MandateSequence Sequence { get; set; }

    [StringLength(500)]
    public string? Note { get; set; }
}

/// <param name="IbanEnding">Last four characters of the IBAN (the list does not show the whole number).</param>
public sealed record MandateRow(
    Guid Id,
    Guid LeaseId,
    string RentalUnit,
    string Property,
    string Tenant,
    string Reference,
    string DebtorName,
    string IbanEnding,
    DateOnly SignedOn,
    MandateSequence Sequence,
    DateOnly? RevokedOn,
    bool Active);

public sealed record MandateLeaseOption(Guid Id, string Label);

/// <summary>
/// Rent to collect by SEPA direct debit, and the file for it (<c>pain.008.001.08</c>). The file is uploaded in the online banking of the landlord;
/// Elocaro books nothing and sends nothing to a bank. The payments show up in the bank import afterwards.
/// </summary>
public interface IDirectDebits
{
    /// <summary>
    /// Open monthly rents of leases paid by direct debit that fall due in the coming weeks, grouped by lease and due date, with the reason if one cannot be
    /// collected yet (no valid mandate). The amount is the open amount of the rent after payments.
    /// </summary>
    Task<IReadOnlyList<DebitCandidate>> CandidatesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The file for the chosen candidates (their <see cref="DebitCandidate.Key"/>). Amount, account and mandate are read again from the data. The collection date
    /// has to be at least one day away and not before the due date. Every person whose IBAN goes into the file is logged as an access.
    /// </summary>
    Task<PaymentOrderResult> CreateFileAsync(IReadOnlyList<string> keys, DateOnly collectionDate, CancellationToken cancellationToken = default);
}

/// <param name="Key">Stable identification for the selection: <c>debit:{leaseId}:{due}</c>.</param>
/// <param name="Problem">Why it cannot be put into a file yet (German), otherwise <c>null</c>.</param>
public sealed record DebitCandidate(string Key, Guid LeaseId, string Tenant, string RentalUnit, string? MandateReference, decimal Amount, DateOnly Due, string? Problem);
