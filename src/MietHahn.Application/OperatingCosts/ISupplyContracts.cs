using System.ComponentModel.DataAnnotations;
using MietHahn.Application.Common;
using MietHahn.Domain.Common;
using MietHahn.Domain.Heating;
using MietHahn.Domain.Metering;
using MietHahn.Domain.OperatingCosts;

namespace MietHahn.Application.OperatingCosts;

/// <summary>
/// Supply contracts (water, sewage, electricity, gas) of a property: the cost of a billing period is calculated from the consumption of
/// the chosen meters and the tariffs. Invoices arrive months late and advance invoices are no costs.
/// </summary>
public interface ISupplyContracts
{
    Task<SupplyOverview?> GetAsync(Guid propertyId, CancellationToken cancellationToken = default);

    /// <summary>Creates the contract with its first tariff.</summary>
    Task<Result> CreateAsync(Guid propertyId, SupplyContractInput input, CancellationToken cancellationToken = default);

    /// <summary>New tariff from the effective date; the previous one ends the day before.</summary>
    Task<Result> AddTariffAsync(Guid contractId, SupplyTariffInput input, CancellationToken cancellationToken = default);

    /// <summary>Corrects the contract data (the tariffs stay).</summary>
    Task<Result> UpdateAsync(Guid contractId, SupplyContractInput input, CancellationToken cancellationToken = default);

    /// <summary>Corrects a tariff (start, prices, note).</summary>
    Task<Result> UpdateTariffAsync(Guid contractId, Guid tariffId, SupplyTariffInput input, CancellationToken cancellationToken = default);

    Task<Result> RemoveTariffAsync(Guid contractId, Guid tariffId, CancellationToken cancellationToken = default);

    Task<Result> RemoveAsync(Guid contractId, CancellationToken cancellationToken = default);
}

public sealed record SupplyOverview(Guid PropertyId, string PropertyName, IReadOnlyList<SupplyContractView> Contracts, IReadOnlyList<MeterOption> Meters);

/// <param name="IsHouseMeter">Not assigned to a unit or a room (main meter).</param>
public sealed record MeterOption(Guid Id, string Label, MeterType Type, bool IsHouseMeter);

public sealed record SupplyContractView(
    Guid Id,
    string Name,
    string? Supplier,
    string CostTypeName,
    string CostType,
    MeterType MeterType,
    SupplyBasis Basis,
    PriceUnit PriceUnit,
    HeatingCostKind? HeatingKind,
    DeviationHandling Deviation,
    UnitMeterScope UnitMeters,
    IReadOnlyList<string> Meters,
    IReadOnlyList<SupplyTariffView> Tariffs,
    IReadOnlyList<Guid>? MeterIds = null);

public sealed record SupplyTariffView(Guid Id, Period Validity, decimal UnitPrice, decimal BaseFeePerMonth, string? Note);

public sealed class SupplyContractInput
{
    [Required(ErrorMessage = "Bitte eine Bezeichnung angeben.")]
    [StringLength(100)]
    public string Name { get; set; } = "";

    [StringLength(100)]
    public string? Supplier { get; set; }

    [Required(ErrorMessage = "Bitte die Kostenart wählen.")]
    public string CostType { get; set; } = "";

    public MeterType MeterType { get; set; }

    public SupplyBasis Basis { get; set; }

    public PriceUnit PriceUnit { get; set; }

    public HeatingCostKind? HeatingKind { get; set; }

    public DeviationHandling Deviation { get; set; }

    public UnitMeterScope UnitMeters { get; set; } = UnitMeterScope.WithHotWater;

    public List<Guid> MeterIds { get; set; } = [];

    public SupplyTariffInput Tariff { get; set; } = new();
}

public sealed class SupplyTariffInput
{
    [Required(ErrorMessage = "Bitte angeben, ab wann der Tarif gilt.")]
    public DateOnly? From { get; set; }

    [Range(typeof(decimal), "0", "100", ParseLimitsInInvariantCulture = true, ErrorMessage = "Der Preis liegt zwischen 0 und 100 € je Einheit. Bitte das Komma als Dezimalzeichen eingeben, z. B. 0,0905.")]
    public decimal? UnitPrice { get; set; }

    [Range(typeof(decimal), "0", "1000", ParseLimitsInInvariantCulture = true, ErrorMessage = "Der Grundpreis liegt zwischen 0 und 1.000 € je Monat.")]
    public decimal? BaseFeePerMonth { get; set; }

    [StringLength(200)]
    public string? Note { get; set; }
}
