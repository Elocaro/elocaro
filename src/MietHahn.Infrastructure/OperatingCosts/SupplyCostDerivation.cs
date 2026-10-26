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
            hints.AddRange(DeviationHints(contract, source, billingPeriod));
            foreach (var part in result.Parts.Where(p => p.Amount != 0))
            {
                var fuel = contract.HeatingKind == HeatingCostKind.Fuel;
                var amount = part.Amount;
                if (contract.Deviation == DeviationHandling.Landlord && contract.Basis == SupplyBasis.MainMeter
                    && Deviation(contract, source, part.Period) is { Main: > 0m, Units: > 0m } deviation && deviation.Units < deviation.Main)
                {
                    // The landlord bears the share of the difference: tenants pay for what their meters measured.
                    amount = decimal.Round(part.Amount * deviation.Units / deviation.Main, 2, MidpointRounding.AwayFromZero);
                    costs.Add(new BillingCost(
                        Guid.NewGuid(),
                        contract.CostType,
                        $"{part.Description}: Anteil der Differenz zwischen Hauptzähler ({deviation.Main:0.###}) und Zählern der Einheiten ({deviation.Units:0.###}), trägt der Vermieter",
                        part.Amount - amount,
                        part.Period,
                        false));
                }

                costs.Add(new BillingCost(
                    part.TariffId,
                    contract.CostType,
                    part.Description,
                    amount,
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

    /// <summary>Main meter vs. sum of the meters assigned to units in the period (meter units); <c>null</c> if not determinable or not applicable.</summary>
    private static (decimal Main, decimal Units)? Deviation(SupplyContract contract, MeterConsumptionSource source, Period period)
    {
        var main = source.MetersQuantity(contract.MeterIds, period);
        var units = source.AssignedQuantity(contract.MeterType, period);
        return main.Value is { } m && units.Value is { } u && u > 0m ? (m, u) : null;
    }

    /// <summary>Plausibility check: the main meter and the sum of the unit meters should be close (leaks, garden, meter tolerance).</summary>
    private static List<string> DeviationHints(SupplyContract contract, MeterConsumptionSource source, Period period)
    {
        var hints = new List<string>();
        if (contract.Basis != SupplyBasis.MainMeter || Deviation(contract, source, period) is not { } d || d.Main <= 0m)
        {
            return hints;
        }

        var difference = d.Main - d.Units;
        var percent = Math.Abs(difference) / d.Main * 100m;
        var text = $"{contract.Name}: Hauptzähler {d.Main:N2} und Zähler der Einheiten zusammen {d.Units:N2} (Differenz {difference:N2}, {percent:0.#} %)";
        if (difference < 0m && percent > DeviationTolerancePercent)
        {
            hints.Add($"Plausibilität: {text}. Die Zähler der Einheiten zeigen mehr als der Hauptzähler: Stände und Zählerzuordnung prüfen.");
        }
        else if (percent > DeviationTolerancePercent)
        {
            hints.Add($"Plausibilität: {text}. Auffällig: mögliche Leckage, Gartenverbrauch oder fehlende Zähler. {(contract.Deviation == DeviationHandling.Landlord ? "Den Anteil der Differenz trägt der Vermieter." : "Die Differenz wird auf die Einheiten nach Verbrauch verteilt (im Vertrag umstellbar auf „trägt der Vermieter“).")}");
        }

        return hints;
    }

    /// <summary>Difference above which the plausibility check reports (meter tolerance and common use stay below).</summary>
    private const decimal DeviationTolerancePercent = 10m;

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
