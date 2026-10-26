using System.ComponentModel.DataAnnotations;
using MietHahn.Application.Common;
using MietHahn.Domain.Tax;

namespace MietHahn.Application.Tax;

/// <summary>Acquisition data, subsequent costs and movable assets of a property and the resulting depreciation plan (AfA).</summary>
public interface IDepreciationService
{
    /// <summary>Data and plan; <c>null</c> if the property does not exist.</summary>
    Task<DepreciationOverview?> GetAsync(Guid propertyId, CancellationToken cancellationToken = default);

    Task<Result> SaveAcquisitionAsync(Guid propertyId, AcquisitionInput input, CancellationToken cancellationToken = default);

    Task<Result> AddCostAsync(Guid propertyId, CostInput input, CancellationToken cancellationToken = default);

    Task<Result> UpdateCostAsync(Guid propertyId, Guid costId, CostInput input, CancellationToken cancellationToken = default);

    Task<Result> RemoveCostAsync(Guid propertyId, Guid costId, CancellationToken cancellationToken = default);

    Task<Result> AddAssetAsync(Guid propertyId, AssetInput input, CancellationToken cancellationToken = default);

    Task<Result> UpdateAssetAsync(Guid propertyId, Guid assetId, AssetInput input, CancellationToken cancellationToken = default);

    Task<Result> RemoveAssetAsync(Guid propertyId, Guid assetId, CancellationToken cancellationToken = default);
}

public sealed class AcquisitionInput
{
    [Required(ErrorMessage = "Bitte das Kaufdatum angeben.")]
    public DateOnly? PurchaseDate { get; set; }

    [Required(ErrorMessage = "Bitte den Kaufpreis angeben.")]
    [Range(0.01, 1_000_000_000, ErrorMessage = "Der Kaufpreis muss größer als 0 sein.")]
    public decimal? PurchasePrice { get; set; }

    [Range(0, 100_000_000)]
    public decimal? Notary { get; set; }

    [Range(0, 100_000_000)]
    public decimal? LandRegistry { get; set; }

    [Range(0, 100_000_000)]
    public decimal? TransferTax { get; set; }

    [Range(0, 100_000_000)]
    public decimal? Broker { get; set; }

    [Required(ErrorMessage = "Bitte den Anteil Grund und Boden angeben.")]
    [Range(0, 99.99, ErrorMessage = "Der Anteil muss zwischen 0 und unter 100 % liegen.")]
    public decimal? LandSharePercent { get; set; }

    [Required(ErrorMessage = "Bitte das Baujahr bzw. Fertigstellungsjahr angeben.")]
    [Range(1700, 2100, ErrorMessage = "Bitte ein gültiges Jahr angeben.")]
    public int? CompletionYear { get; set; }

    [Range(0.01, 100, ErrorMessage = "Der Satz muss zwischen 0 und 100 % liegen.")]
    public decimal? RateOverridePercent { get; set; }

    [Range(0.01, 100, ErrorMessage = "Der Satz muss zwischen 0 und 100 % liegen.")]
    public decimal? SpecialRatePercent { get; set; }

    [Range(1, 20)]
    public int SpecialYears { get; set; } = 4;

    [StringLength(2000)]
    public string? Notes { get; set; }
}

public sealed class CostInput
{
    [Required(ErrorMessage = "Bitte das Datum angeben.")]
    public DateOnly? Date { get; set; }

    [Required(ErrorMessage = "Bitte den Nettobetrag angeben.")]
    [Range(0.01, 1_000_000_000, ErrorMessage = "Der Betrag muss größer als 0 sein.")]
    public decimal? Amount { get; set; }

    [Required(ErrorMessage = "Bitte eine Beschreibung angeben.")]
    [StringLength(200)]
    public string Description { get; set; } = "";

    /// <summary>Renovation/modernisation (counts for the 15 % limit) instead of production cost to be capitalised.</summary>
    public bool IsMaintenance { get; set; }
}

public sealed class AssetInput
{
    [Required(ErrorMessage = "Bitte eine Bezeichnung angeben.")]
    [StringLength(200)]
    public string Name { get; set; } = "";

    [Required(ErrorMessage = "Bitte das Anschaffungsdatum angeben.")]
    public DateOnly? AcquisitionDate { get; set; }

    [Required(ErrorMessage = "Bitte die Anschaffungskosten angeben.")]
    [Range(0.01, 1_000_000_000, ErrorMessage = "Die Anschaffungskosten müssen größer als 0 sein.")]
    public decimal? Cost { get; set; }

    [Range(1, 100, ErrorMessage = "Die Nutzungsdauer muss zwischen 1 und 100 Jahren liegen.")]
    public int UsefulLifeYears { get; set; } = 10;
}

public sealed record CostRow(Guid Id, DateOnly Date, decimal Amount, string Description, bool IsMaintenance);

public sealed record AssetRow(Guid Id, string Name, DateOnly AcquisitionDate, decimal Cost, int UsefulLifeYears);

public sealed record DepreciationOverview(
    Guid PropertyId,
    string PropertyName,
    AcquisitionInput? Acquisition,
    IReadOnlyList<CostRow> Costs,
    IReadOnlyList<AssetRow> Assets,
    DepreciationPlan? Plan);
