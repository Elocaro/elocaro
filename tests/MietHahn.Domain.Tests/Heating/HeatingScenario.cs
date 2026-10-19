using MietHahn.Domain.Common;
using MietHahn.Domain.Heating;
using MietHahn.Domain.Metering;
using MietHahn.Domain.OperatingCosts;
using static MietHahn.Domain.Tests.OperatingCosts.Scenario;

namespace MietHahn.Domain.Tests.Heating;

/// <summary>Helpers for the heating cost tests.</summary>
internal static class HeatingScenario
{
    public static BillingCost HeatCost(
        int id,
        string type,
        decimal amount,
        HeatingCostKind kind,
        Period? period = null,
        int? receipt = null,
        decimal? quantity = null,
        string? unit = null,
        decimal? co2Kg = null,
        string description = "Rechnung") =>
        new(
            Id(id),
            type,
            description,
            amount,
            period ?? Year2026,
            true,
            HeatingKind: kind,
            ReceiptId: receipt is { } r ? Id(1000 + r) : null,
            FuelQuantity: quantity,
            FuelUnit: unit,
            Co2Kg: co2Kg);

    /// <summary>Runs the operating cost core with the heating calculator plugged in.</summary>
    public static BillingResult Run(BillingInput input, HeatingParameters? parameters = null) =>
        OperatingCostCalculator.Calculate(input, [new HeatingCostCalculator(parameters ?? new HeatingParameters())]);

    public static TenantCostLine HeatingLine(BillingResult result, int lease, string type) =>
        result.Tenants.Single(t => t.LeaseId == Id(lease)).Lines.Single(l => l.CostType == type);

    public static GroupBalance HeatingBalance(BillingResult result, int lease) =>
        result.Tenants.Single(t => t.LeaseId == Id(lease)).Groups.Single(g => g.Group == CostGroup.Heating);
}

/// <summary>Consumption source with a rule per meter type: unit and period give the quantity.</summary>
internal sealed class RuleConsumption(Func<Guid, MeterType, Period, ConsumptionQuantity> rule) : IConsumptionSource
{
    public ConsumptionQuantity Quantity(Guid unitId, MeterType type, Period period) => rule(unitId, type, period);

    /// <summary>Constant annual quantities per unit for the whole billing period; shorter periods are not expected.</summary>
    public static RuleConsumption Totals(Period billing, params (int Unit, MeterType Type, decimal Value)[] values) =>
        new((unit, type, period) =>
            period == billing && values.FirstOrDefault(v => Id(v.Unit) == unit && v.Type == type) is { Value: var value, Unit: not 0 }
                ? new ConsumptionQuantity(value)
                : new ConsumptionQuantity(null, false, "Kein Zähler zugeordnet."));
}
