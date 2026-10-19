using System.ComponentModel.DataAnnotations;
using MietHahn.Application.Common;
using MietHahn.Domain.Receipts;

namespace MietHahn.Application.Receipts;

/// <summary>Eingangsrechnungen mit Dateien und Aufteilung in Kostenpositionen.</summary>
public interface IReceiptService
{
    Task<IReadOnlyList<ReceiptRow>> SearchAsync(ReceiptQuery query, CancellationToken cancellationToken = default);

    Task<ReceiptDetails?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Legt den Beleg zu bereits gespeicherten Dateien an, optional gleich mit einer Position über den ganzen Betrag.</summary>
    Task<Result> CreateAsync(ReceiptInput data, IReadOnlyList<Guid> fileIds, CostItemInput? position = null, CancellationToken cancellationToken = default);

    Task<Result> UpdateAsync(Guid id, ReceiptInput data, CancellationToken cancellationToken = default);

    Task<Result> AddFilesAsync(Guid id, IReadOnlyList<Guid> fileIds, CancellationToken cancellationToken = default);

    Task<Result> RemoveFileAsync(Guid id, Guid fileId, CancellationToken cancellationToken = default);

    Task<Result> AddItemAsync(Guid id, CostItemInput input, CancellationToken cancellationToken = default);

    Task<Result> RemoveCostItemAsync(Guid id, Guid positionId, CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task<ReceiptOptions> OptionsAsync(CancellationToken cancellationToken = default);
}

/// <param name="Year">Rechnungsdatum oder Leistungszeitraum im Jahr.</param>
/// <param name="Unpaid">Weder Zahlungsdatum noch zugeordneter Umsatz.</param>
public sealed record ReceiptQuery(
    int? Year = null,
    Guid? PropertyId = null,
    string? CostType = null,
    bool Unpaid = false,
    bool Unallocated = false,
    string? Text = null);

public sealed class ReceiptInput
{
    public Guid? SupplierId { get; set; }

    [StringLength(200)]
    public string? SupplierName { get; set; }

    [StringLength(100)]
    public string? InvoiceNumber { get; set; }

    [Required(ErrorMessage = "Bitte das Rechnungsdatum angeben.")]
    public DateOnly? InvoiceDate { get; set; }

    public DateOnly? ServiceFrom { get; set; }

    public DateOnly? ServiceTo { get; set; }

    [Required(ErrorMessage = "Bitte den Rechnungsbetrag (brutto) angeben.")]
    [Range(typeof(decimal), "-1000000", "1000000", ParseLimitsInInvariantCulture = true)]
    public decimal? Gross { get; set; }

    [Range(typeof(decimal), "-1000000", "1000000", ParseLimitsInInvariantCulture = true)]
    public decimal? Net { get; set; }

    [Range(typeof(decimal), "-1000000", "1000000", ParseLimitsInInvariantCulture = true)]
    public decimal? Vat { get; set; }

    public DateOnly? DueOn { get; set; }

    public DateOnly? PaidOn { get; set; }

    [Range(typeof(decimal), "0", "100000000", ParseLimitsInInvariantCulture = true)]
    public decimal? FuelQuantity { get; set; }

    [StringLength(10)]
    public string? FuelUnit { get; set; }

    [Range(typeof(decimal), "0", "100000000", ParseLimitsInInvariantCulture = true)]
    public decimal? Co2Kg { get; set; }

    [StringLength(2000)]
    public string? Note { get; set; }
}

public sealed class CostItemInput
{
    [Required(ErrorMessage = "Bitte die Kostenart wählen.")]
    public string CostType { get; set; } = "";

    /// <summary>Leer = noch nicht aufgeteilter Rest.</summary>
    [Range(typeof(decimal), "-1000000", "1000000", ParseLimitsInInvariantCulture = true)]
    public decimal? Amount { get; set; }

    [Required(ErrorMessage = "Bitte das Objekt wählen.")]
    public Guid? PropertyId { get; set; }

    public Guid? UnitId { get; set; }

    /// <summary>Leer = Leistungszeitraum bzw. Kalenderjahr des Rechnungsdatums.</summary>
    public DateOnly? From { get; set; }

    public DateOnly? To { get; set; }

    /// <summary>Leer = laut Kostenart.</summary>
    public bool? Allocable { get; set; }

    public Section35a Section35a { get; set; }

    [Range(typeof(decimal), "0", "1000000", ParseLimitsInInvariantCulture = true)]
    public decimal? LaborCosts { get; set; }

    [StringLength(500)]
    public string? Note { get; set; }
}

public sealed record ReceiptRow(
    Guid Id,
    string Supplier,
    string? InvoiceNumber,
    DateOnly InvoiceDate,
    decimal Gross,
    decimal Allocated,
    IReadOnlyList<string> CostTypes,
    IReadOnlyList<string> Properties,
    DateOnly? PaidOn,
    int Files);

public sealed record ReceiptDetails(
    Guid Id,
    ReceiptInput Data,
    string Supplier,
    IReadOnlyList<ReceiptFileView> Files,
    IReadOnlyList<CostItemView> Items,
    decimal Allocated,
    decimal Open);

public sealed record ReceiptFileView(Guid FileId, string Name, string MimeType, int Page);

public sealed record CostItemView(
    Guid Id,
    string CostType,
    string CostTypeText,
    decimal Amount,
    string Property,
    string? Unit,
    Domain.Common.Period BillingPeriod,
    bool Allocable,
    Section35a Section35a,
    decimal? LaborCosts,
    string? Note);

public sealed record ReceiptOptions(IReadOnlyList<ReferenceOption> Suppliers, IReadOnlyList<ReferenceOption> Properties, IReadOnlyList<ReferenceOption> Units);

public sealed record ReferenceOption(Guid Id, string Name);
