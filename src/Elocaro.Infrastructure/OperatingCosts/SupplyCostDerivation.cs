using Elocaro.Domain.Common;
using Elocaro.Domain.Heating;
using Elocaro.Domain.Metering;
using Elocaro.Domain.OperatingCosts;

namespace Elocaro.Infrastructure.OperatingCosts;

/// <summary>
/// Replaces the receipts of a cost type by the cost calculated from a supply contract (consumption × tariff): invoices arrive months
/// late and advance invoices are no costs. An annual statement of the supplier (<c>SupplierStatement</c>) wins over the calculation.
/// </summary>
internal static class SupplyCostDerivation
{
    public sealed record Outcome(List<BillingCost> Costs, List<string> Hints, List<Application.OperatingCosts.MeterCheck> Checks);

    public static Outcome Apply(
        IReadOnlyList<SupplyContract> contracts,
        IReadOnlyList<BillingCost> receiptCosts,
        IReadOnlySet<Guid> statementReceipts,
        MeterConsumptionSource source,
        Period billingPeriod,
        decimal? fuelKwhPerUnit,
        HeatingSettings? heating = null,
        IReadOnlyList<(Guid Id, string Name)>? units = null)
    {
        var costs = receiptCosts.ToList();
        var hints = new List<string>();
        var checks = new List<Application.OperatingCosts.MeterCheck>();
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
                var advances = replaceable.Sum(c => c.Amount);
                var calculated = result.Parts.Sum(p => p.Amount);
                var balance = calculated - advances;
                hints.Add($"{contract.Name}: {replaceable.Count} Beleg(e) ({Money(advances)}) werden nicht angesetzt, die Kosten ergeben sich aus Verbrauch und Tarif ({Money(calculated)}). Eine Jahresabrechnung des Versorgers ersetzt die Berechnung."
                    + (balance == 0m ? "" : $" Gegen die Abschläge ergibt das voraussichtlich {(balance > 0m ? "eine Nachzahlung" : "ein Guthaben")} von {Money(Math.Abs(balance))} beim Versorger."));
            }
            else
            {
                hints.Add($"{contract.Name}: Die Kosten ({Money(result.Parts.Sum(p => p.Amount))}) sind aus Verbrauch und Tarif berechnet, es liegt noch keine Jahresabrechnung vor.");
            }

            var fuelUnit = source.FuelUnit(contract.MeterIds);
            hints.AddRange(DeviationHints(contract, source, billingPeriod));
            checks.Add(Check(contract, source, billingPeriod, units ?? []));
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

                var (co2Kg, carbonCost) = fuel ? Carbon(part, fuelUnit, fuelKwhPerUnit, heating, billingPeriod, hints) : (null, 0m);
                costs.Add(new BillingCost(
                    part.TariffId,
                    contract.CostType,
                    part.Description,
                    amount - carbonCost,
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
                    co2Kg));
                if (carbonCost > 0m)
                {
                    costs.Add(new BillingCost(Guid.NewGuid(), contract.CostType, $"CO₂-Kosten (aus dem Gaspreis, {co2Kg:#,##0} kg)", carbonCost, part.Period, true, null, Domain.Receipts.Section35a.None, null, null, HeatingCostKind.CarbonPrice));
                }
            }
        }

        return new Outcome([.. costs.OrderBy(c => c.BillingPeriod.From).ThenBy(c => c.Id)], hints, checks);
    }

    /// <summary>
    /// CO₂ quantity of a fuel part (kWh × emission factor) and, if the CO₂ price of the year is known, the CO₂ costs contained in the gas price;
    /// they are split off the fuel costs so that nothing is counted twice.
    /// </summary>
    private static (decimal? Kg, decimal Cost) Carbon(SupplyCostPart part, string? fuelUnit, decimal? kwhPerUnit, HeatingSettings? heating, Period billingPeriod, List<string> hints)
    {
        if (heating?.CarbonKgPerKwhOrDefault is not { } factor || part.MeterQuantity is not { } quantity || !heating.ApplyCarbonCosts)
        {
            return (null, 0m);
        }

        var kwh = fuelUnit switch
        {
            "kWh" => quantity,
            "MWh" => quantity * 1000m,
            _ => kwhPerUnit is { } perUnit ? quantity * perUnit : (decimal?)null,
        };
        if (kwh is not { } energy)
        {
            return (null, 0m);
        }

        var kg = decimal.Round(energy * factor, 1);
        var price = heating.EnergyYears.FirstOrDefault(e => e.Year == billingPeriod.To!.Value.Year)?.CarbonPricePerTon;
        if (price is not { } perTon)
        {
            hints.Add($"CO₂: Die Menge ({kg:#,##0} kg) ist aus dem Verbrauch berechnet. Für {billingPeriod.To!.Value.Year} fehlt der CO₂-Preis je Tonne (Heizungseinstellungen), die CO₂-Kosten und damit die Aufteilung nach CO2KostAufG können nicht berechnet werden.");
            return (kg, 0m);
        }

        return (kg, decimal.Round(kg / 1000m * perTon, 2, MidpointRounding.AwayFromZero));
    }

    /// <summary>Main meter vs. sum of the meters assigned to units in the period (meter units); <c>null</c> if not determinable or not applicable.</summary>
    /// <summary>
    /// Consumption of the unit meters that the main meter of the contract also counts. The hot water of the flats is heated from the water
    /// of the cold water main meter, so for cold water the hot water meters of the units are added.
    /// </summary>
    private static ConsumptionQuantity Assigned(SupplyContract contract, MeterConsumptionSource source, Period period)
    {
        var cold = source.AssignedQuantity(contract.MeterType, period);
        if (contract.MeterType != MeterType.ColdWater || contract.UnitMeters != UnitMeterScope.WithHotWater || cold.Value is not { } coldValue)
        {
            return cold;
        }

        var hot = source.AssignedQuantity(MeterType.HotWater, period);
        return hot.Value is { } hotValue ? new ConsumptionQuantity(coldValue + hotValue, cold.Interpolated || hot.Interpolated) : hot;
    }

    private static (decimal Main, decimal Units)? Deviation(SupplyContract contract, MeterConsumptionSource source, Period period)
    {
        var main = source.MetersQuantity(contract.MeterIds, period);
        var units = Assigned(contract, source, period);
        return main.Value is { } m && units.Value is { } u && u > 0m ? (m, u) : null;
    }

    private static Application.OperatingCosts.MeterCheck Check(SupplyContract contract, MeterConsumptionSource source, Period period, IReadOnlyList<(Guid Id, string Name)> units)
    {
        var consumption = units
            .Select(u => (u.Name, Quantity: source.Quantity(u.Id, contract.MeterType, period).Value))
            .Where(x => x.Quantity is > 0m)
            .Select(x => new Application.OperatingCosts.UnitConsumptionRow(x.Name, x.Quantity!.Value))
            .ToList();
        if (contract.Basis != SupplyBasis.MainMeter || Deviation(contract, source, period) is not { } d || d.Main <= 0m)
        {
            return new Application.OperatingCosts.MeterCheck(contract.Name, contract.MeterType.ToString(), null, null, null, Application.OperatingCosts.CheckLevel.Ok, consumption);
        }

        var percent = (d.Main - d.Units) / d.Main * 100m;
        var level = percent < -DeviationTolerancePercent ? Application.OperatingCosts.CheckLevel.Error
            : percent > DeviationTolerancePercent ? Application.OperatingCosts.CheckLevel.Warning
            : Application.OperatingCosts.CheckLevel.Ok;
        return new Application.OperatingCosts.MeterCheck(contract.Name, contract.MeterType.ToString(), d.Main, d.Units, decimal.Round(percent, 1), level, consumption);
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
            var assigned = Assigned(contract, source, period);
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
