using System.ComponentModel.DataAnnotations;
using Elocaro.Application.Common;
using Elocaro.Domain.Heating;

namespace Elocaro.Application.Heating;

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

    /// <summary>Brennwert and Zustandszahl per billing year (gas).</summary>
    [Range(typeof(decimal), "0.0001", "1", ErrorMessage = "Der Emissionsfaktor liegt zwischen 0 und 1 kg CO₂ je kWh.", ParseLimitsInInvariantCulture = true)]
    public decimal? CarbonKgPerKwh { get; set; }

    public List<FuelEnergyYearInput> EnergyYears { get; set; } = [];

    public Guid? OilTankMeterId { get; set; }

    public OilValuation OilValuation { get; set; }

    [Range(typeof(decimal), "0", "100", ErrorMessage = "Bitte einen Preis zwischen 0 und 100 €/l angeben.", ParseLimitsInInvariantCulture = true)]
    public decimal? OilOpeningPricePerLiter { get; set; }

    public bool ApplyCarbonCosts { get; set; }

    public CarbonSpecialCase CarbonSpecialCase { get; set; }

    [StringLength(500)]
    public string? CarbonNote { get; set; }
}

public sealed class FuelEnergyYearInput
{
    [Range(2000, 2100, ErrorMessage = "Bitte ein Jahr zwischen 2000 und 2100 angeben.")]
    public int Year { get; set; }

    [Range(typeof(decimal), "5", "20", ErrorMessage = "Der Brennwert liegt zwischen 5 und 20 kWh/m³.", ParseLimitsInInvariantCulture = true)]
    public decimal? CalorificValue { get; set; }

    [Range(typeof(decimal), "0.5", "1.2", ErrorMessage = "Die Zustandszahl liegt zwischen 0,5 und 1,2.", ParseLimitsInInvariantCulture = true)]
    public decimal? StateNumber { get; set; }

    [Range(typeof(decimal), "0", "500", ErrorMessage = "Der CO₂-Preis liegt zwischen 0 und 500 € je Tonne.", ParseLimitsInInvariantCulture = true)]
    public decimal? CarbonPricePerTon { get; set; }

    [StringLength(200)]
    public string? Note { get; set; }
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
