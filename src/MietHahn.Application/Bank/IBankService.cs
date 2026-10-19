using System.ComponentModel.DataAnnotations;
using MietHahn.Application.Common;
using MietHahn.Domain.Bank;

namespace MietHahn.Application.Bank;

/// <summary>Bank accounts, file import (camt.053, MT940, CSV) and transactions.</summary>
public interface IBankService
{
    Task<IReadOnlyList<BankAccountRow>> AccountsAsync(CancellationToken cancellationToken = default);

    Task<BankAccountInput?> GetAccountAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result> CreateAccountAsync(BankAccountInput data, CancellationToken cancellationToken = default);

    Task<Result> UpdateAccountAsync(Guid id, BankAccountInput data, CancellationToken cancellationToken = default);

    /// <summary>Reads the file, stores it until confirmation and returns the preview id.</summary>
    Task<Result> CreatePreviewAsync(Guid bankAccountId, string fileName, byte[] content, string? profile = null, CancellationToken cancellationToken = default);

    Task<ImportPreview?> PreviewAsync(Guid bankImportId, CancellationToken cancellationToken = default);

    /// <summary>Adopts the new transactions; already existing ones (same fingerprint) are skipped.</summary>
    Task<Result> ImportAsync(Guid bankImportId, CancellationToken cancellationToken = default);

    Task<Result> DiscardAsync(Guid bankImportId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ImportRow>> ImportsAsync(Guid bankAccountId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TransactionRow>> TransactionsAsync(TransactionQuery query, CancellationToken cancellationToken = default);

    Task<Result> IgnoreAsync(Guid transactionId, string? note, CancellationToken cancellationToken = default);

    Task<Result> ReopenAsync(Guid transactionId, CancellationToken cancellationToken = default);

    /// <summary>Bundled and custom CSV profiles.</summary>
    Task<IReadOnlyList<CsvProfileInput>> CsvProfilesAsync(CancellationToken cancellationToken = default);

    Task<Result> SaveCsvProfileAsync(Guid? id, CsvProfileInput data, CancellationToken cancellationToken = default);

    Task<Result> DeleteCsvProfileAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed class BankAccountInput
{
    [Required(ErrorMessage = "Bitte einen Namen angeben.")]
    [StringLength(100)]
    public string Name { get; set; } = "";

    [Required(ErrorMessage = "Bitte die IBAN angeben.")]
    [StringLength(42)]
    public string Iban { get; set; } = "";

    [StringLength(11)]
    public string? Bic { get; set; }

    [StringLength(100)]
    public string? Bank { get; set; }

    public AccountType Type { get; set; }

    public List<Guid> PropertyIds { get; set; } = [];
}

public sealed record BankAccountRow(Guid Id, string Name, string Iban, string? Bank, AccountType Type, IReadOnlyList<string> Properties, int OpenTransactions, DateOnly? LastTransaction);

/// <param name="DifferentIban">According to its account details, the file belongs to a different IBAN.</param>
public sealed record ImportPreview(
    Guid Id,
    Guid BankAccountId,
    string Account,
    string FileName,
    BankFormat Format,
    string? Profile,
    ImportStatus Status,
    string? DifferentIban,
    IReadOnlyList<PreviewRow> Rows,
    IReadOnlyList<string> Hints);

public sealed record PreviewRow(ParsedTransaction Transaction, bool Duplicate);

public sealed record ImportRow(Guid Id, DateTimeOffset Timestamp, string? CreatedBy, string FileName, BankFormat Format, string? Profile, ImportStatus Status, int Read, int Added, int Duplicates, DateOnly? From, DateOnly? To);

public sealed record TransactionQuery(Guid? BankAccountId = null, TransactionStatus? Status = null, string? Text = null);

public sealed record TransactionRow(
    Guid Id,
    Guid BankAccountId,
    DateOnly BookingDate,
    DateOnly? ValueDate,
    decimal Amount,
    string? CounterpartyName,
    string? CounterpartyIban,
    string Purpose,
    string? BookingText,
    string? EndToEndId,
    string? MandateReference,
    string? CreditorId,
    TransactionStatus Status,
    string? Note);

public sealed class CsvProfileInput
{
    public Guid? Id { get; set; }

    public bool IsDefault { get; set; }

    [Required(ErrorMessage = "Bitte einen Namen angeben.")]
    [StringLength(100)]
    public string Name { get; set; } = "";

    [Required]
    [StringLength(1)]
    public string Delimiter { get; set; } = ";";

    public string Charset { get; set; } = "auto";

    [Required]
    [StringLength(100)]
    public string DateFormat { get; set; } = "dd.MM.yyyy|dd.MM.yy";

    [Required]
    [StringLength(1)]
    public string DecimalSeparator { get; set; } = ",";

    [Required(ErrorMessage = "Bitte die Spalte mit dem Buchungsdatum angeben.")]
    [StringLength(200)]
    public string BookingDateColumn { get; set; } = "";

    [StringLength(200)]
    public string? ValueDateColumn { get; set; }

    [StringLength(200)]
    public string? AmountColumn { get; set; }

    [StringLength(200)]
    public string? DebitColumn { get; set; }

    [StringLength(200)]
    public string? CreditColumn { get; set; }

    [StringLength(200)]
    public string? NameColumn { get; set; }

    [StringLength(200)]
    public string? IbanColumn { get; set; }

    [StringLength(200)]
    public string? BicColumn { get; set; }

    [StringLength(200)]
    public string? PurposeColumn { get; set; }

    [StringLength(200)]
    public string? BookingTextColumn { get; set; }

    [StringLength(200)]
    public string? EndToEndColumn { get; set; }

    [StringLength(200)]
    public string? MandateReferenceColumn { get; set; }

    [StringLength(200)]
    public string? CreditorIdColumn { get; set; }
}
