using System.ComponentModel.DataAnnotations;
using MietHahn.Application.Common;
using MietHahn.Domain.Finance;
using MietHahn.Domain.Leases;

namespace MietHahn.Application.Finance;

/// <summary>Sollstellungen, Zahlungen und Offene Posten je Mietverhältnis.</summary>
public interface ITenantAccounts
{
    Task<TenantAccount?> GetAsync(Guid leaseId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Erzeugt und gleicht die Monatsmieten ab dem Buchhaltungsbeginn bis zum laufenden Monat an (idempotent).
    /// Ohne Vertrag für alle Verträge. Liefert die Anzahl neuer, geänderter und entfernter Sollstellungen.
    /// </summary>
    Task<int> GenerateChargesAsync(Guid? leaseId = null, CancellationToken cancellationToken = default);

    Task<Result> CreateChargeAsync(Guid leaseId, ChargeInput input, CancellationToken cancellationToken = default);

    /// <summary>Nur von Hand angelegte Sollstellungen; Monatsmieten ergeben sich aus dem Vertrag.</summary>
    Task<Result> DeleteChargeAsync(Guid chargeId, CancellationToken cancellationToken = default);

    Task<Result> RecordPaymentAsync(Guid leaseId, PaymentInput input, CancellationToken cancellationToken = default);

    /// <summary>Nur von Hand erfasste Zahlungen; Bankumsätze werden über die Zuordnung gelöst.</summary>
    Task<Result> DeletePaymentAsync(Guid paymentId, CancellationToken cancellationToken = default);

    /// <summary>Fällige, nicht (vollständig) bezahlte Forderungen über alle Mietverhältnisse.</summary>
    Task<IReadOnlyList<OpenItem>> OpenItemsAsync(CancellationToken cancellationToken = default);
}

public interface IFinanceSettings
{
    Task<FinanceSettingsInput> GetAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(FinanceSettingsInput data, CancellationToken cancellationToken = default);
}

public sealed class FinanceSettingsInput
{
    /// <summary>Erster Monat mit automatischen Monatsmieten; frühere Rückstände als Saldovortrag erfassen.</summary>
    public DateOnly? AccountingStart { get; set; }

    public AllocationOrder Allocation { get; set; }
}

public sealed class ChargeInput
{
    public ChargeType Kind { get; set; } = ChargeType.AdditionalPayment;

    [Required(ErrorMessage = "Bitte das Fälligkeitsdatum angeben.")]
    public DateOnly? Due { get; set; }

    /// <summary>Immer positiv eingeben; Erstattungen werden als Guthaben gebucht.</summary>
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

    /// <summary>Auszahlung an den Mieter (z. B. Guthaben überwiesen) statt Zahlungseingang.</summary>
    public bool Payout { get; set; }

    [StringLength(500)]
    public string? Purpose { get; set; }

    /// <summary>Tilgungsbestimmung: diese Forderung zuerst.</summary>
    public Guid? ChargeId { get; set; }
}

public enum EntryType
{
    Charge = 0,
    Payment = 1,
}

/// <summary>Eine Zeile im Mieterkonto; <see cref="Debit"/> und <see cref="Credit"/> sind positiv.</summary>
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

/// <summary>Je Jahr und Mietbestandteil: Soll nach Leistungsmonat, davon getilgt, und Zufluss nach Zahlungsdatum (§ 11 EStG).</summary>
public sealed record AnnualValue(int Year, RentComponentType Component, decimal Debit, decimal Settled, decimal Inflow);

/// <param name="Balance">Positiv = Rückstand des Mieters, negativ = Guthaben.</param>
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
