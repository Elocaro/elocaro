using MietHahn.Domain.Common;
using MietHahn.Domain.Heating;
using MietHahn.Domain.Metering;
using MietHahn.Domain.OperatingCosts;

namespace MietHahn.Infrastructure.OperatingCosts;

/// <summary>
/// Replaces the receipts of a cost type by the cost calculated from a supply contract (consumption × tariff): invoices arrive months
/// late and advance invoices are no costs. An annual statement of the supplier (<c>SupplierStatement</c>) wins over the calculation.
/// </summary>
internal static class SupplyCostDerivation
{
    public sealed record Outcome(List<BillingCost> Costs, List<string> Hints);

    public static Outcome Apply(
        IReadOnlyList<SupplyContract> contracts,
        IReadOnlyList<BillingCost> receiptCosts,
        IReadOnlySet<Guid> statementReceipts,
        MeterConsumptionSource source,
        Period billingPeriod,
        decimal? fuelKwhPerUnit)
    {
        var costs = receiptCosts.ToList();
        var hints = new List<string>();
        foreach (var contract in contracts.OrderBy(c => c.CostType).ThenBy(c => c.Name, StringComparer.Ordinal))
        {
            var replaceable = costs.Where(c => Matches(contract, c)).ToList();
            var statement = replaceable.Where(c => c.ReceiptId is { } id && statementReceipts.Contains(id)).ToList();
            if (statement.Count > 0)
            {
                var advance = replaceable.Except(statement).ToList();
                if (advance.Count > 0)
                {
                    costs.RemoveAll(advance.Contains);
                    hints.Add($"{contract.Name}: Es liegt eine Jahresabrechnung des Versorgers vor; {advance.Count} Abschlagsbeleg(e) ({Money(advance.Sum(c => c.Amount))}) werden nicht angesetzt.");
                }
                else
                {
                    hints.Add($"{contract.Name}: Es gilt die Jahresabrechnung des Versorgers ({Money(statement.Sum(c => c.Amount))}), die Berechnung aus dem Tarif entfällt.");
                }

                continue;
            }

            var result = SupplyCostCalculator.Calculate(contract.ToModel(), billingPeriod, period => Quantity(contract, source, period, fuelKwhPerUnit));
            if (result.Parts.Count == 0)
            {
                hints.Add($"{contract.Name}: Der Versorgungsvertrag hat für den Zeitraum keinen Tarif, es gelten die Belege.");
                continue;
            }

            if (!result.CoversWholePeriod)
            {
                hints.Add($"{contract.Name}: Der Tarif deckt den Abrechnungszeitraum nicht ganz ab, es gelten die Belege (Tarif ergänzen).");
                continue;
            }

            if (result.Problems.Count > 0)
            {
                hints.AddRange(result.Problems.Select(p => $"Fehlt: {p} Es gelten die Belege."));
                continue;
            }

            if (replaceable.Count > 0)
            {
                costs.RemoveAll(replaceable.Contains);
                hints.Add($"{contract.Name}: {replaceable.Count} Beleg(e) ({Money(replaceable.Sum(c => c.Amount))}) werden nicht angesetzt, die Kosten ergeben sich aus Verbrauch und Tarif ({Money(result.Parts.Sum(p => p.Amount))}). Eine Jahresabrechnung des Versorgers ersetzt die Berechnung.");
            }
            else
            {
                hints.Add($"{contract.Name}: Die Kosten ({Money(result.Parts.Sum(p => p.Amount))}) sind aus Verbrauch und Tarif berechnet, es liegt noch keine Jahresabrechnung vor.");
            }

            var fuelUnit = source.FuelUnit(contract.MeterIds);
            foreach (var part in result.Parts.Where(p => p.Amount != 0))
            {
                var fuel = contract.HeatingKind == HeatingCostKind.Fuel;
                costs.Add(new BillingCost(
                    part.TariffId,
                    contract.CostType,
                    part.Description,
                    part.Amount,
                    part.Period,
                    true,
                    null,
                    Domain.Receipts.Section35a.None,
                    null,
                    null,
                    contract.HeatingKind,
                    part.TariffId,
                    fuel ? part.MeterQuantity : null,
                    fuel ? fuelUnit : null,
                    null));
            }
        }

        return new Outcome([.. costs.OrderBy(c => c.BillingPeriod.From).ThenBy(c => c.Id)], hints);
    }

    private static bool Matches(SupplyContract contract, BillingCost cost) =>
        cost.CostType == contract.CostType
        && (contract.HeatingKind is null
            || cost.HeatingKind == contract.HeatingKind
            || (cost.HeatingKind is null && contract.HeatingKind == HeatingCostKind.Fuel && cost.FuelQuantity is not null));

    private static (decimal? Quantity, decimal? MeterQuantity, string? Missing) Quantity(SupplyContract contract, MeterConsumptionSource source, Period period, decimal? fuelKwhPerUnit)
    {
        var main = source.MetersQuantity(contract.MeterIds, period);
        if (main.Value is not { } value)
        {
            return (null, null, main.Note);
        }

        if (contract.Basis == SupplyBasis.CommonArea)
        {
            var assigned = source.AssignedQuantity(contract.MeterType, period);
            if (assigned.Value is not { } flats)
            {
                return (null, null, assigned.Note);
            }

            value -= flats;
            if (value < 0)
            {
                return (null, null, "Die Zähler der Wohnungen zeigen mehr Verbrauch als der Hauptzähler.");
            }
        }

        if (contract.PriceUnit == PriceUnit.KilowattHour && contract.MeterType == MeterType.Gas && fuelKwhPerUnit is null)
        {
            return (null, null, "In den Heizungseinstellungen fehlt der Energieinhalt je Einheit (kWh je m³ Gas).");
        }

        var quantity = contract.PriceUnit == PriceUnit.KilowattHour && contract.MeterType == MeterType.Gas ? value * fuelKwhPerUnit!.Value : value;
        return (quantity, value, null);
    }

    private static string Money(decimal amount) => $"{amount:N2} €";
}
