using MietHahn.Domain.Receipts;
using MietHahn.Domain.OperatingCosts;

namespace MietHahn.Domain.Heating;

/// <summary>Opening and closing stock of the oil tank in liters; <c>null</c> = reading missing.</summary>
public sealed record OilStock(decimal? OpeningLiters, decimal? ClosingLiters, string? Note = null);

/// <summary>Settings and measured values the heating cost calculation needs beyond the common billing input.</summary>
public sealed record HeatingParameters
{
    /// <summary>Share of the consumption-based part in percent (50 to 70).</summary>
    public decimal ConsumptionPercent { get; init; } = 70m;

    public HeatingFuel Fuel { get; init; }

    /// <summary>Heating and hot water come from one plant: cost types 4 and 6 are split according to § 9 HeizkostenV.</summary>
    public bool JointPlant { get; init; } = true;

    /// <summary>Heat quantity of the hot water circuit in kWh from the heat meter; <c>null</c> = no meter, the formula applies.</summary>
    public ConsumptionQuantity? HotWaterHeat { get; init; }

    public HotWaterSplitBasis SplitBasis { get; init; }

    /// <summary>Heat quantity of the heating meters of the units in kWh (for <see cref="HotWaterSplitBasis.MeterSum"/>); <c>null</c> = not determined.</summary>
    public ConsumptionQuantity? HeatingHeat { get; init; }

    /// <summary>Factor of the formula Q = factor · V · (t − 10 °C) for the heat quantity of the hot water (§ 9 para. 2).</summary>
    public decimal HotWaterFormulaFactor { get; init; } = 2.5m;

    public decimal HotWaterTemperature { get; init; } = 60m;

    /// <summary>kWh per unit of fuel for receipts that do not state kWh.</summary>
    public decimal? FuelKwhPerUnit { get; init; }

    public OilStock? Oil { get; init; }

    public OilValuation OilValuation { get; init; }

    public decimal? OilOpeningPricePerLiter { get; init; }

    public bool ApplyCarbonCosts { get; init; } = true;

    public CarbonSpecialCase CarbonSpecialCase { get; init; }
}

/// <summary>
/// Calculation core for the heating and hot water costs (HeizkostenV) and the CO₂ cost split (CO2KostAufG), plugged into the
/// operating cost calculation as <see cref="ICostGroupCalculator"/>. Pure function without database access.
/// <list type="number">
/// <item>Total costs of the plant (fuel, operating power, maintenance, emission measurement, cleaning, meter services, CO₂ costs).</item>
/// <item>Split into heating and hot water (§ 9): heat meter in the hot water circuit, otherwise the formula Q = 2.5 · V · (t − 10).</item>
/// <item>Distribution: 50 to 70 % by consumption (heat meters kWh; hot water m³), the rest by area (heated area; living area).</item>
/// <item>Tenant change: interim reading preferred, otherwise degree days (heating) or days (hot water).</item>
/// <item>CO₂ cost split by level; the landlord's share is deducted from the tenants' heating costs and shown separately.</item>
/// </list>
/// </summary>
public sealed class HeatingCostCalculator(HeatingParameters parameters) : ICostGroupCalculator
{
    public const string HeatKey = "heating-heat";
    public const string HotWaterKey = "heating-hotwater";
    public const string CarbonKey = "heating-co2";

    public CostGroup Group => CostGroup.Heating;

    public GroupResult Calculate(BillingInput input, IReadOnlyList<BillingCost> costs)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(costs);
        return new HeatingRun(input, costs, parameters).Execute();
    }
}
