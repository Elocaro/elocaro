using MietHahn.Domain.Common;

namespace MietHahn.Domain.Heating;

/// <summary>Energy source of the central plant: decides how the fuel consumption is determined and whether CO₂ costs apply.</summary>
public enum HeatingFuel
{
    /// <summary>Natural gas: the consumption in kWh comes from the utility bill (receipt).</summary>
    Gas = 0,

    /// <summary>Heating oil: consumption = opening stock + deliveries − closing stock of the tank.</summary>
    Oil = 1,

    /// <summary>Not fossil or not metered by fuel (district heat, pellets ...): quantity in kWh from the receipt, no CO₂ cost split.</summary>
    Other = 2,
}

/// <summary>Valuation of the oil consumption.</summary>
public enum OilValuation
{
    /// <summary>Weighted average price of opening stock and deliveries.</summary>
    WeightedAverage = 0,

    /// <summary>First in, first out: the oldest stock is consumed first.</summary>
    Fifo = 1,
}

/// <summary>Special cases of the CO2KostAufG that are only shown as a warning (not calculated).</summary>
public enum CarbonSpecialCase
{
    None = 0,

    /// <summary>Listed building or conservation area: the law may reduce the landlord's share (to be checked).</summary>
    ProtectedBuilding = 1,

    /// <summary>Very efficient building (e.g. passive house standard): level 1 anyway, special rules to be checked.</summary>
    HighlyEfficient = 2,

    /// <summary>The building is possibly not subject to the CO2KostAufG (e.g. no fossil supply).</summary>
    NotSubject = 3,
}

/// <summary>
/// Settings for the heating cost statement (HeizkostenV) of a property: share of the consumption-based part, source of the
/// hot water share, energy source and the CO₂ special cases. Defaults: 70 % consumption, 30 % base costs.
/// </summary>
public sealed class HeatingSettings : Entity
{
    public const decimal MinimumConsumptionPercent = 50m;
    public const decimal MaximumConsumptionPercent = 70m;

    private HeatingSettings()
    {
    }

    public HeatingSettings(Guid propertyId)
    {
        PropertyId = propertyId;
    }

    public Guid PropertyId { get; private set; }

    public HeatingFuel Fuel { get; set; }

    /// <summary>Share of the costs that is distributed by consumption (50 to 70 %, § 7 para. 1 and § 8 para. 1 HeizkostenV); the rest by area.</summary>
    public decimal ConsumptionPercent { get; set; } = 70m;

    /// <summary>One plant supplies heating and hot water: the costs of the plant are split according to § 9 HeizkostenV.</summary>
    public bool JointPlant { get; set; } = true;

    /// <summary>Heat meter in the hot water circuit (§ 9 para. 2); without it the formula Q = factor · V · (t − 10) applies.</summary>
    public Guid? HotWaterHeatMeterId { get; set; }

    /// <summary>Factor of the formula for the heat quantity of the hot water (kWh per m³ and Kelvin); 2.5 as stated in § 9 para. 2.</summary>
    public decimal HotWaterFormulaFactor { get; set; } = 2.5m;

    /// <summary>Average temperature of the hot water in °C for the formula.</summary>
    public decimal HotWaterTemperature { get; set; } = 60m;

    /// <summary>Energy content per unit of fuel (kWh per l, m³ ...) if the receipt has no quantity in kWh; oil: calorific value, default 10 kWh/l.</summary>
    public decimal? FuelKwhPerUnit { get; set; }

    /// <summary>Tank meter (stock in liters) for oil; opening and closing stock are its readings.</summary>
    public Guid? OilTankMeterId { get; set; }

    public OilValuation OilValuation { get; set; }

    /// <summary>Price per liter of the opening stock (€/l); without it the price of the first delivery applies.</summary>
    public decimal? OilOpeningPricePerLiter { get; set; }

    /// <summary>CO₂ cost split is applied (only for fossil fuels).</summary>
    public bool ApplyCarbonCosts { get; set; } = true;

    public CarbonSpecialCase CarbonSpecialCase { get; set; }

    /// <summary>Free note on the special case (e.g. the decision of the authority), shown on the settings page.</summary>
    public string? CarbonNote { get; set; }

    public void Validate()
    {
        if (ConsumptionPercent is < MinimumConsumptionPercent or > MaximumConsumptionPercent)
        {
            throw new RuleViolationException("Der verbrauchsabhängige Anteil muss zwischen 50 und 70 % liegen (§ 7 Abs. 1 HeizkostenV); eine Abrechnung nur nach Verbrauch ist nicht zulässig.");
        }

        if (HotWaterFormulaFactor is <= 0 or > 10)
        {
            throw new RuleViolationException("Der Faktor der Warmwasserformel muss zwischen 0 und 10 liegen (Standard 2,5).");
        }

        if (HotWaterTemperature is <= 10 or > 95)
        {
            throw new RuleViolationException("Die Warmwassertemperatur muss zwischen 10 und 95 °C liegen (Standard 60 °C).");
        }

        if (FuelKwhPerUnit is <= 0)
        {
            throw new RuleViolationException("Der Energieinhalt je Einheit muss größer als 0 sein.");
        }

        if (OilOpeningPricePerLiter is < 0)
        {
            throw new RuleViolationException("Der Preis des Anfangsbestands darf nicht negativ sein.");
        }
    }
}
