using System.ComponentModel.DataAnnotations;
using MietHahn.Application.Common;
using MietHahn.Domain.Heating;

namespace MietHahn.Application.Heating;

/// <summary>Settings of a property for the heating cost statement (HeizkostenV) and the CO₂ cost split (CO2KostAufG).</summary>
public interface IHeatingSettings
{
    /// <summary>Settings of the property (defaults if none are saved yet); <c>null</c> if the property does not exist.</summary>
    Task<HeatingSettingsView?> GetAsync(Guid propertyId, CancellationToken cancellationToken = default);

    Task<Result> SaveAsync(Guid propertyId, HeatingSettingsInput input, CancellationToken cancellationToken = default);
}

public sealed class HeatingSettingsInput
{
    public HeatingFuel Fuel { get; set; }

    [Required(ErrorMessage = "Bitte den verbrauchsabhängigen Anteil angeben.")]
    [Range(typeof(decimal), "50", "70", ErrorMessage = "Der verbrauchsabhängige Anteil muss zwischen 50 und 70 % liegen (§ 7 Abs. 1 HeizkostenV); 100 % Verbrauch ist nicht zulässig.", ParseLimitsInInvariantCulture = true)]
    public decimal? ConsumptionPercent { get; set; } = 70m;

    /// <summary>One plant for heating and hot water. No default here: an unchecked box is not posted, so the form shows the defaults via the view.</summary>
    public bool JointPlant { get; set; }

    /// <summary>Heat meter in the hot water circuit; empty = formula of § 9 para. 2.</summary>
    public Guid? HotWaterHeatMeterId { get; set; }

    public HotWaterSplitBasis SplitBasis { get; set; }

    [Range(typeof(decimal), "0.1", "10", ErrorMessage = "Der Faktor liegt zwischen 0,1 und 10 (Standard 2,5).", ParseLimitsInInvariantCulture = true)]
    public decimal? HotWaterFormulaFactor { get; set; } = 2.5m;

    [Range(typeof(decimal), "11", "95", ErrorMessage = "Die Temperatur liegt zwischen 11 und 95 °C (Standard 60 °C).", ParseLimitsInInvariantCulture = true)]
    public decimal? HotWaterTemperature { get; set; } = 60m;

    [Range(typeof(decimal), "0.0001", "1000", ErrorMessage = "Bitte einen Energieinhalt größer als 0 angeben.", ParseLimitsInInvariantCulture = true)]
    public decimal? FuelKwhPerUnit { get; set; }

    public Guid? OilTankMeterId { get; set; }

    public OilValuation OilValuation { get; set; }

    [Range(typeof(decimal), "0", "100", ErrorMessage = "Bitte einen Preis zwischen 0 und 100 €/l angeben.", ParseLimitsInInvariantCulture = true)]
    public decimal? OilOpeningPricePerLiter { get; set; }

    public bool ApplyCarbonCosts { get; set; }

    public CarbonSpecialCase CarbonSpecialCase { get; set; }

    [StringLength(500)]
    public string? CarbonNote { get; set; }
}

public sealed record MeterOption(Guid Id, string Name);

/// <param name="Saved"><c>false</c> = no settings saved yet, the input shows the defaults.</param>
public sealed record HeatingSettingsView(
    Guid PropertyId,
    string PropertyName,
    bool Saved,
    HeatingSettingsInput Input,
    IReadOnlyList<MeterOption> HeatMeters,
    IReadOnlyList<MeterOption> OilMeters,
    string CarbonTableVersion,
    string CarbonTableStatus,
    string DegreeDayTableVersion,
    string DegreeDayTableStatus);
