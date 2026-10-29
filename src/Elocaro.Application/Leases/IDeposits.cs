using System.ComponentModel.DataAnnotations;
using Elocaro.Application.Common;
using Elocaro.Domain.Deposits;

namespace Elocaro.Application.Leases;

/// <summary>The deposit of a lease as an account: payments, interest, refund and set-offs.</summary>
public interface IDeposits
{
    /// <summary><c>null</c> if the lease does not exist.</summary>
    Task<DepositView?> GetAsync(Guid leaseId, CancellationToken cancellationToken = default);

    Task<Result> AddAsync(Guid leaseId, DepositEntryInput input, CancellationToken cancellationToken = default);

    /// <summary>Corrects an entry (kind, date, amount, note).</summary>
    Task<Result> UpdateAsync(Guid entryId, DepositEntryInput input, CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(Guid entryId, CancellationToken cancellationToken = default);
}

public sealed class DepositEntryInput
{
    public DepositEntryKind Kind { get; set; } = DepositEntryKind.Received;

    [Required(ErrorMessage = "Bitte das Datum angeben.")]
    public DateOnly? Date { get; set; }

    [Required(ErrorMessage = "Bitte den Betrag angeben.")]
    [Range(typeof(decimal), "0.01", "1000000", ErrorMessage = "Bitte einen Betrag über 0 € angeben.", ParseLimitsInInvariantCulture = true)]
    public decimal? Amount { get; set; }

    [StringLength(500)]
    public string? Note { get; set; }
}

public sealed record DepositRow(Guid Id, DepositEntryKind Kind, DateOnly Date, decimal Amount, string? Note);

/// <param name="Note">The note of the lease about the form of the deposit (e.g. savings account, guarantee).</param>
/// <param name="LeaseEnd">End of the lease, if known.</param>
public sealed record DepositView(Guid LeaseId, string RentalUnit, string Tenant, DepositAccount Account, string? Note, DateOnly? LeaseEnd, IReadOnlyList<DepositRow> Entries);
