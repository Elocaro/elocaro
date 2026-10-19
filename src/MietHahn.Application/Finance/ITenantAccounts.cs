using System.ComponentModel.DataAnnotations;
using MietHahn.Application.Common;
using MietHahn.Domain.Finance;
using MietHahn.Domain.Leases;

namespace MietHahn.Application.Finance;

/// <summary>Charges, payments and open items per tenancy.</summary>
public interface ITenantAccounts
{
    Task<TenantAccount?> GetAsync(Guid leaseId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates and adjusts the monthly rents from the start of accounting up to the current month (idempotent).
    /// Without a lease, for all leases. Returns the number of new, changed and removed charges.
    /// </summary>
    Task<int> GenerateChargesAsync(Guid? leaseId = null, CancellationToken cancellationToken = default);

    Task<Result> CreateChargeAsync(Guid leaseId, ChargeInput input, CancellationToken cancellationToken = default);

    /// <summary>Only manually created charges; monthly rents result from the lease.</summary>
    Task<Result> DeleteChargeAsync(Guid chargeId, CancellationToken cancellationToken = default);

    Task<Result> RecordPaymentAsync(Guid leaseId, PaymentInput input, CancellationToken cancellationToken = default);

    /// <summary>Only manually recorded payments; bank transactions are resolved via the assignment.</summary>
    Task<Result> DeletePaymentAsync(Guid paymentId, CancellationToken cancellationToken = default);

    /// <summary>Due, not (fully) paid claims across all tenancies.</summary>
    Task<IReadOnlyList<OpenItem>> OpenItemsAsync(CancellationToken cancellationToken = default);
}

public interface IFinanceSettings
{
    Task<FinanceSettingsInput> GetAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(FinanceSettingsInput data, CancellationToken cancellationToken = default);
}

public sealed class FinanceSettingsInput
{
    /// <summary>First month with automatic monthly rents; record earlier arrears as a balance carried forward.</summary>
    public DateOnly? AccountingStart { get; set; }

    public AllocationOrder Allocation { get; set; }
}

public sealed class ChargeInput
{
    public ChargeType Kind { get; set; } = ChargeType.AdditionalPayment;

    [Required(ErrorMessage = "Bitte das Fälligkeitsdatum angeben.")]
    public DateOnly? Due { get; set; }

    /// <summary>Always enter as positive; refunds are posted as credits.</summary>
    [Required(ErrorMessage = "Bitte den Betrag angeben.")]
    [Range(typeof(decimal), "0.01", "1000000", ErrorMessage = "Bitte einen Betrag über 0 € angeben.", ParseLimitsInInvariantCulture = true)]
    public decimal? Amount { get; set; }

    [StringLength(200)]
    public string? Name { get; set; }

    [StringLength(1000)]
    public string? Note { get; set; }
}

public sealed class PaymentInput
{
    [Required(ErrorMessage = "Bitte das Zahlungsdatum angeben.")]
    public DateOnly? Date { get; set; }

    [Required(ErrorMessage = "Bitte den Betrag angeben.")]
    [Range(typeof(decimal), "0.01", "1000000", ErrorMessage = "Bitte einen Betrag über 0 € angeben.", ParseLimitsInInvariantCulture = true)]
    public decimal? Amount { get; set; }

    /// <summary>Payout to the tenant (e.g. credit transferred) instead of an incoming payment.</summary>
    public bool Payout { get; set; }

    [StringLength(500)]
    public string? Purpose { get; set; }

    /// <summary>Designation of payment: settle this claim first.</summary>
    public Guid? ChargeId { get; set; }
}

public enum EntryType
{
    Charge = 0,
    Payment = 1,
}

/// <summary>A row in the tenant account; <see cref="Debit"/> and <see cref="Credit"/> are positive.</summary>
public sealed record AccountEntry(
    Guid Id,
    EntryType EntryType,
    DateOnly Date,
    string Text,
    ChargeType? Kind,
    decimal Debit,
    decimal Credit,
    decimal Balance,
    decimal? Open,
    bool Automatic,
    PaymentOrigin? Origin);

/// <summary>Per year and rent component: debit by service month, of which settled, and inflow by payment date (§ 11 EStG).</summary>
public sealed record AnnualValue(int Year, RentComponentType Component, decimal Debit, decimal Settled, decimal Inflow);

/// <param name="Balance">Positive = tenant's arrears, negative = credit.</param>
public sealed record TenantAccount(
    Guid LeaseId,
    string RentalUnit,
    string Tenant,
    DateOnly? AccountingStart,
    IReadOnlyList<AccountEntry> Entries,
    decimal Balance,
    decimal Due,
    IReadOnlyList<AnnualValue> AnnualValues,
    AllocationOrder Allocation);

public sealed record OpenItem(
    Guid ChargeId,
    Guid LeaseId,
    string RentalUnit,
    string Tenant,
    ChargeType Kind,
    string Text,
    DateOnly Due,
    decimal Amount,
    decimal Open,
    int DaysOverdue);
