using System.Globalization;
using Elocaro.Domain.Common;
using Elocaro.Domain.Metering;
using Elocaro.Domain.OperatingCosts;
using Elocaro.Domain.Receipts;

namespace Elocaro.Domain.Heating;

/// <summary>One run of the heating cost calculation; holds the intermediate state.</summary>
internal sealed partial class HeatingRun
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    private const decimal DefaultOilKwhPerLiter = 10m;

    private readonly BillingInput input;
    private readonly IReadOnlyList<BillingCost> costs;
    private readonly HeatingParameters settings;
    private readonly Period billing;
    private readonly DateOnly billingEnd;
    private readonly int billingDays;
    private readonly List<BillingUnit> units;
    private readonly List<BillingLease> leases;
    private readonly Dictionary<Guid, BillingUnit> unitsById;
    private readonly List<CalculationProblem> problems = [];
    private readonly HashSet<string> problemKeys = [];
    private readonly Dictionary<(Guid, MeterType, Period), ConsumptionQuantity> consumptionCache = [];
    private readonly Dictionary<Guid, decimal> flatRateActuals = [];

    public HeatingRun(BillingInput input, IReadOnlyList<BillingCost> costs, HeatingParameters settings)
    {
        this.input = input;
        this.costs = costs;
        this.settings = settings;
        billing = input.BillingPeriod;
        billingEnd = billing.To ?? throw new RuleViolationException("Der Abrechnungszeitraum braucht ein Ende.");
        billingDays = billing.Days;
        units = [.. input.Units.OrderBy(u => u.Name, StringComparer.Ordinal).ThenBy(u => u.Id)];
        unitsById = units.ToDictionary(u => u.Id);
        leases = [];
        foreach (var lease in input.Leases.Where(l => l.Term.Overlaps(billing)).OrderBy(l => l.Term.From).ThenBy(l => l.Id))
        {
            if (unitsById.TryGetValue(lease.UnitId, out var unit) && (unit.Rooms.Count == 0 || unit.Rooms.Any(r => r.Id == lease.RoomId)))
            {
                leases.Add(lease);
            }
        }
    }

    private enum Pool
    {
        Heat,
        HotWater,
        Joint,
    }

    private sealed class Item(BillingCost cost, decimal prorated, decimal factor, HeatingCostKind kind, Pool pool, string calculation)
    {
        public BillingCost Cost { get; } = cost;

        public decimal Prorated { get; set; } = prorated;

        public decimal Factor { get; } = factor;

        public HeatingCostKind Kind { get; } = kind;

        public Pool Pool { get; } = pool;

        public string Calculation { get; set; } = calculation;
    }

    private sealed record ReceiptData(Item Representative, decimal? Quantity, string? Unit, decimal? Co2Kg, IReadOnlyList<Item> FuelItems);

    private sealed record Split(decimal Ratio, ProtocolEntry? Entry);

    private static string Money(decimal value) => value.ToString("#,##0.00 €", German);

    private static string Num(decimal value) => value.ToString("#,##0.###", German);

    private static string Pct(decimal fraction) => (fraction * 100m).ToString("#,##0.##", German) + " %";

    private static string Day(DateOnly value) => value.ToString("dd.MM.yyyy", German);

    private static string Span(Period period) => period.To is { } to ? $"{Day(period.From)} – {Day(to)}" : $"ab {Day(period.From)}";

    private static string KindName(HeatingCostKind kind) => kind switch
    {
        HeatingCostKind.Fuel => "Brennstoff",
        HeatingCostKind.OperatingPower => "Betriebsstrom",
        HeatingCostKind.Maintenance => "Wartung",
        HeatingCostKind.EmissionMeasurement => "Abgasmessung",
        HeatingCostKind.Cleaning => "Reinigung",
        HeatingCostKind.MeterService => "Zählermiete und Eichung",
        HeatingCostKind.CarbonPrice => "CO₂-Kosten",
        _ => "Sonstige Kosten",
    };

    private void Problem(ProblemSeverity severity, string text, string? costType = null)
    {
        if (problemKeys.Add($"{severity}|{costType}|{text}"))
        {
            problems.Add(new CalculationProblem(severity, text, costType));
        }
    }

    private ConsumptionQuantity Consumption(Guid unitId, MeterType type, Period period)
    {
        if (!consumptionCache.TryGetValue((unitId, type, period), out var quantity))
        {
            quantity = input.Consumption.Quantity(unitId, type, period);
            consumptionCache[(unitId, type, period)] = quantity;
        }

        return quantity;
    }

    public GroupResult Execute()
    {
        if (settings.ConsumptionPercent is < HeatingSettings.MinimumConsumptionPercent or > HeatingSettings.MaximumConsumptionPercent)
        {
            Problem(
                ProblemSeverity.Error,
                $"Der verbrauchsabhängige Anteil von {Num(settings.ConsumptionPercent)} % ist nicht zulässig: Es müssen 50 bis 70 % sein (§ 7 Abs. 1 HeizkostenV).",
                HeatingCostCalculator.HeatKey);
        }

        var items = PrepareItems();
        var receipts = ReceiptsOf(items);
        var fuel = DetermineFuel(items, receipts);
        var split = SplitHotWater(items, fuel.Kwh);

        var heatExact = items.Sum(i => Part(i, Pool.Heat, split.Ratio));
        var waterExact = items.Sum(i => Part(i, Pool.HotWater, split.Ratio));
        var carbonHeat = items.Where(i => i.Kind == HeatingCostKind.CarbonPrice).Sum(i => Part(i, Pool.Heat, split.Ratio));
        var carbonWater = items.Where(i => i.Kind == HeatingCostKind.CarbonPrice).Sum(i => Part(i, Pool.HotWater, split.Ratio));
        var totalCents = CentDistribution.Cents(heatExact + waterExact);
        var heatCents = CentDistribution.Cents(heatExact);
        var waterCents = totalCents - heatCents;

        var parties = BuildParties(heatExact != 0m, waterExact != 0m);
        var heat = Distribute(
            "Heizkosten (Heizung)", HeatingCostCalculator.HeatKey, items, Pool.Heat, split, heatExact, heatCents, parties,
            Basis.HeatConsumption, Basis.HeatArea, "kWh Wärme", "Wärmemenge", "Heizfläche");
        var water = Distribute(
            "Warmwasserkosten (Warmwasser)", HeatingCostCalculator.HotWaterKey, items, Pool.HotWater, split, waterExact, waterCents, parties,
            Basis.WaterConsumption, Basis.WaterArea, "m³ Warmwasser", "Warmwasserverbrauch", "Wohnfläche");

        var carbon = Carbon(fuel, heat, water, carbonHeat, carbonWater);
        var sections = new List<GroupSection> { SummarySection(items, fuel, split, heatExact, waterExact, totalCents, heat, water) };
        if (carbon?.Section is { } carbonSection)
        {
            sections.Add(carbonSection);
        }

        var costTypes = new List<CostTypeResult>();
        var landlord = new List<LandlordPosition>();
        var tenantLines = new List<GroupTenantLine>();
        foreach (var outcome in new[] { heat, water })
        {
            if (outcome is null)
            {
                continue;
            }

            costTypes.Add(outcome.Result);
            landlord.AddRange(outcome.Result.Landlord);
            tenantLines.AddRange(outcome.Lines.Select(kv => new GroupTenantLine(kv.Key, kv.Value)));
        }

        if (carbon?.Result is { } carbonResult)
        {
            costTypes.Add(carbonResult);
            landlord.AddRange(carbonResult.Landlord);
            tenantLines.AddRange(carbon.Lines.Select(kv => new GroupTenantLine(kv.Key, kv.Value)));
        }

        WarnAboutFlatRates();
        return new GroupResult(costTypes, tenantLines, landlord, problems, flatRateActuals, sections);
    }

    // ---- costs -----------------------------------------------------------------------------------------------------

    private List<Item> PrepareItems()
    {
        var list = new List<Item>();
        foreach (var cost in costs.Where(c => !c.BillingPeriod.IsOpenEnded).OrderBy(c => c.BillingPeriod.From).ThenBy(c => c.Id))
        {
            var overlap = cost.BillingPeriod.DaysIn(billing);
            if (overlap == 0)
            {
                continue;
            }

            var days = cost.BillingPeriod.Days;
            var calculation = overlap == days ? $"{Money(cost.Amount)} (vollständig im Zeitraum)" : $"{Money(cost.Amount)} × {overlap}/{days} Tage";
            var kind = cost.HeatingKind ?? (cost.FuelQuantity is null ? HeatingCostKind.Other : HeatingCostKind.Fuel);
            var pool = cost.CostType switch
            {
                "betrkv-05" => Pool.HotWater,
                "betrkv-06" => Pool.Joint,
                _ => settings.JointPlant ? Pool.Joint : Pool.Heat,
            };
            list.Add(new Item(cost, cost.Amount * overlap / days, (decimal)overlap / days, kind, pool, calculation));
        }

        return list;
    }

    private static decimal Part(Item item, Pool target, decimal ratio) => item.Pool switch
    {
        Pool.Joint => item.Prorated * (target == Pool.Heat ? 1m - ratio : ratio),
        Pool.HotWater => target == Pool.HotWater ? item.Prorated : 0m,
        _ => target == Pool.Heat ? item.Prorated : 0m,
    };

    /// <summary>Receipt data (fuel quantity, CO₂) counts once per receipt, prorated like the cost item it is read from.</summary>
    private static List<ReceiptData> ReceiptsOf(List<Item> items) =>
        [.. items
            .Where(i => i.Cost.FuelQuantity is not null || i.Cost.Co2Kg is not null)
            .GroupBy(i => i.Cost.ReceiptId ?? i.Cost.Id)
            .Select(g =>
            {
                var representative = g.OrderBy(i => i.Kind == HeatingCostKind.Fuel ? 0 : 1).ThenBy(i => i.Cost.BillingPeriod.From).ThenBy(i => i.Cost.Id).First();
                return new ReceiptData(
                    representative,
                    representative.Cost.FuelQuantity * representative.Factor,
                    representative.Cost.FuelUnit,
                    representative.Cost.Co2Kg * representative.Factor,
                    [.. g.Where(i => i.Kind == HeatingCostKind.Fuel)]);
            })];

    private sealed record FuelResult(decimal? Kwh, decimal Co2Kg, string Description);

    private decimal? KwhPerUnit(string? unit, string context)
    {
        var normalized = (unit ?? string.Empty).Trim().ToLowerInvariant().Replace("m3", "m³", StringComparison.Ordinal);
        switch (normalized)
        {
            case "":
            case "kwh":
                if (normalized.Length == 0)
                {
                    Problem(ProblemSeverity.Warning, $"{context}: Die Einheit der Brennstoffmenge fehlt; kWh angenommen.", HeatingCostCalculator.HeatKey);
                }

                return 1m;
            case "mwh":
                return 1000m;
            case "l":
            case "liter":
                return settings.FuelKwhPerUnit ?? DefaultOilKwhPerLiter;
            default:
                if (settings.FuelKwhPerUnit is { } factor)
                {
                    return factor;
                }

                Problem(ProblemSeverity.Error, $"{context}: Die Brennstoffmenge ist in „{unit}“ angegeben. Bitte kWh angeben oder in den Heizungseinstellungen den Energieinhalt je Einheit eintragen.", HeatingCostCalculator.HeatKey);
                return null;
        }
    }

    private FuelResult DetermineFuel(List<Item> items, List<ReceiptData> receipts)
    {
        var co2 = receipts.Sum(r => r.Co2Kg ?? 0m);
        if (items.All(i => i.Pool != Pool.Joint || i.Prorated == 0m) && settings.Fuel != HeatingFuel.Oil)
        {
            return new FuelResult(null, co2, "nicht benötigt");
        }

        return settings.Fuel == HeatingFuel.Oil ? OilFuel(items, receipts, co2) : MeteredFuel(receipts, co2);
    }

    private FuelResult MeteredFuel(List<ReceiptData> receipts, decimal co2)
    {
        decimal kwh = 0;
        var parts = new List<string>();
        foreach (var receipt in receipts.Where(r => r.Quantity is > 0))
        {
            var factor = KwhPerUnit(receipt.Unit, receipt.Representative.Cost.Description);
            if (factor is null)
            {
                continue;
            }

            var part = receipt.Quantity!.Value * factor.Value;
            kwh += part;
            parts.Add($"{receipt.Representative.Cost.Description}: {Num(receipt.Quantity.Value)} {(string.IsNullOrWhiteSpace(receipt.Unit) ? "kWh" : receipt.Unit)}{(factor == 1m ? string.Empty : $" × {Num(factor.Value)} kWh")}");
        }

        return new FuelResult(kwh > 0 ? kwh : null, co2, string.Join("; ", parts));
    }

    private FuelResult OilFuel(List<Item> items, List<ReceiptData> receipts, decimal co2)
    {
        var deliveries = new List<OilDelivery>();
        foreach (var receipt in receipts.Where(r => r.Quantity is > 0 && r.FuelItems.Count > 0))
        {
            var unit = (receipt.Unit ?? "l").Trim().ToLowerInvariant();
            if (unit is not ("l" or "liter" or ""))
            {
                Problem(ProblemSeverity.Error, $"{receipt.Representative.Cost.Description}: Heizöl wird in Litern angegeben, nicht in „{receipt.Unit}“.", HeatingCostCalculator.HeatKey);
                continue;
            }

            var date = receipt.Representative.Cost.BillingPeriod.From < billing.From ? billing.From : receipt.Representative.Cost.BillingPeriod.From;
            deliveries.Add(new OilDelivery(date, receipt.Quantity!.Value, receipt.FuelItems.Sum(i => i.Prorated)));
        }

        var delivered = deliveries.Sum(d => d.Liters);
        var kwhPerLiter = settings.FuelKwhPerUnit ?? DefaultOilKwhPerLiter;
        var stock = settings.Oil;
        if (stock?.OpeningLiters is not { } opening || stock.ClosingLiters is not { } closing)
        {
            Problem(
                ProblemSeverity.Error,
                stock?.Note ?? "Für den Öltank fehlt der Bestand zu Beginn oder zum Ende des Zeitraums (Ablesung des Tankzählers erfassen). Ersatzweise werden die gelieferten Mengen als Verbrauch angesetzt.",
                HeatingCostCalculator.HeatKey);
            return new FuelResult(delivered * kwhPerLiter, co2, $"Lieferungen {Num(delivered)} l (Tankbestand fehlt)");
        }

        if (opening + delivered < closing)
        {
            Problem(ProblemSeverity.Error, $"Der Endbestand des Öltanks ({Num(closing)} l) ist größer als Anfangsbestand und Lieferungen ({Num(opening + delivered)} l).", HeatingCostCalculator.HeatKey);
            return new FuelResult(delivered * kwhPerLiter, co2, $"Lieferungen {Num(delivered)} l (Bestände unstimmig)");
        }

        var consumption = OilConsumptionCalculator.Calculate(opening, closing, settings.OilOpeningPricePerLiter, deliveries, settings.OilValuation);
        if (settings.OilOpeningPricePerLiter is null && opening > 0)
        {
            Problem(ProblemSeverity.Warning, "Der Preis des Anfangsbestands an Heizöl fehlt in den Heizungseinstellungen; der Preis der ersten Lieferung wird angesetzt.", HeatingCostCalculator.HeatKey);
        }

        if (co2 > 0)
        {
            Problem(ProblemSeverity.Warning, "Bei Heizöl werden CO₂-Menge und CO₂-Kosten nach den gelieferten Mengen im Zeitraum angesetzt, nicht nach dem Verbrauch aus dem Tank.", HeatingCostCalculator.CarbonKey);
        }

        // The fuel costs are replaced by the valuated consumption: the purchases of the deliveries are scaled to it.
        var fuelItems = items.Where(i => i.Kind == HeatingCostKind.Fuel).ToList();
        var purchases = fuelItems.Sum(i => i.Prorated);
        if (purchases != 0m)
        {
            var scale = consumption.Cost / purchases;
            foreach (var item in fuelItems)
            {
                item.Prorated *= scale;
                item.Calculation += $" × {scale.ToString("0.####", German)} (Bewertung des Tankverbrauchs)";
            }
        }
        else if (consumption.Cost != 0m)
        {
            var synthetic = new BillingCost(
                Guid.Empty, settings.JointPlant ? "betrkv-06" : "betrkv-04", "Heizöl aus dem Tankbestand", consumption.Cost, billing, true, HeatingKind: HeatingCostKind.Fuel);
            items.Add(new Item(synthetic, consumption.Cost, 1m, HeatingCostKind.Fuel, settings.JointPlant ? Pool.Joint : Pool.Heat, $"{Money(consumption.Cost)} (Tankverbrauch ohne Lieferung)"));
        }

        return new FuelResult(consumption.Liters * kwhPerLiter, co2, $"{consumption.Calculation} × {Num(kwhPerLiter)} kWh/l");
    }

    // ---- hot water split (§ 9 HeizkostenV) -------------------------------------------------------------------------

    private Split SplitHotWater(List<Item> items, decimal? fuelKwh)
    {
        if (items.All(i => i.Pool != Pool.Joint || i.Prorated == 0m))
        {
            return new Split(0m, null);
        }

        const string Formula = "Anteil Warmwasser = Wärmemenge des Warmwassers ÷ Brennstoffenergie der gemeinsamen Anlage (§ 9 HeizkostenV)";
        const string MeterFormula = "Anteil Warmwasser = Wärmemenge des Warmwassers ÷ (Wärmemenge der Heizung + Wärmemenge des Warmwassers), jeweils aus Wärmezählern";
        decimal? heatingHeat = null;
        if (settings.SplitBasis == HotWaterSplitBasis.MeterSum)
        {
            if (settings.HeatingHeat?.Value is { } measuredHeating)
            {
                heatingHeat = measuredHeating;
            }
            else
            {
                Problem(ProblemSeverity.Error, $"Wärmezähler der Heizung: {settings.HeatingHeat?.Note ?? "Verbrauch nicht ermittelbar."} Ersatzweise gilt die Brennstoffenergie.", HeatingCostCalculator.HotWaterKey);
            }
        }

        var byMeters = heatingHeat is not null;
        if (!byMeters && (fuelKwh is not { } fuel0 || fuel0 <= 0m))
        {
            Problem(ProblemSeverity.Error, "Die Brennstoffmenge fehlt (Beleg: Menge in kWh; Heizöl: Tankbestand). Ohne sie lässt sich die gemeinsame Anlage nicht in Heizung und Warmwasser aufteilen; die Kosten werden vorläufig der Heizung zugerechnet.", HeatingCostCalculator.HeatKey);
            return new Split(0m, new ProtocolEntry("Aufteilung Heizung/Warmwasser (§ 9 HeizkostenV)", Formula, "Brennstoffmenge fehlt", 0m));
        }

        decimal? heat = null;
        string method;
        if (settings.HotWaterHeat is { } meter)
        {
            if (meter.Value is { } measured)
            {
                heat = measured;
                method = $"Wärmezähler im Warmwasserkreis: {Num(measured)} kWh{(string.IsNullOrWhiteSpace(meter.Note) ? string.Empty : $" ({meter.Note})")}";
            }
            else
            {
                Problem(ProblemSeverity.Error, $"Wärmezähler im Warmwasserkreis: {meter.Note ?? "Verbrauch nicht ermittelbar."} Ersatzweise gilt die Formel.", HeatingCostCalculator.HotWaterKey);
                method = string.Empty;
            }
        }
        else
        {
            method = string.Empty;
        }

        if (heat is null)
        {
            var volume = HotWaterVolume();
            if (volume is null)
            {
                return new Split(0m, new ProtocolEntry("Aufteilung Heizung/Warmwasser (§ 9 HeizkostenV)", Formula, "Warmwasserverbrauch fehlt", 0m));
            }

            heat = settings.HotWaterFormulaFactor * volume * (settings.HotWaterTemperature - 10m);
            method = $"Formel Q = {Num(settings.HotWaterFormulaFactor)} × V × (t − 10): {Num(settings.HotWaterFormulaFactor)} × {Num(volume.Value)} m³ × ({Num(settings.HotWaterTemperature)} − 10) °C = {Num(heat.Value)} kWh";
        }

        if (byMeters)
        {
            var total = heatingHeat!.Value + heat.Value;
            if (total <= 0m)
            {
                Problem(ProblemSeverity.Error, "Die Wärmezähler zeigen keinen Verbrauch; die Kosten werden vorläufig der Heizung zugerechnet.", HeatingCostCalculator.HotWaterKey);
                return new Split(0m, new ProtocolEntry("Aufteilung Heizung/Warmwasser (§ 9 HeizkostenV)", MeterFormula, "Kein Wärmeverbrauch", 0m));
            }

            var meterRatio = heat.Value / total;
            return new Split(
                meterRatio,
                new ProtocolEntry(
                    "Aufteilung Heizung/Warmwasser (§ 9 HeizkostenV)",
                    MeterFormula,
                    $"{method}; Wärme Heizung {Num(heatingHeat.Value)} kWh ({settings.HeatingHeat!.Note}); {Num(heat.Value)} ÷ ({Num(heatingHeat.Value)} + {Num(heat.Value)})",
                    decimal.Round(meterRatio * 100m, 4),
                    "Ergebnis in Prozent. Wärmeverluste der Anlage (Kessel, Leitungen) werden bei dieser Basis nicht dem Warmwasser zugerechnet.")
                { Unit = "%" });
        }

        var fuel = fuelKwh!.Value;
        var ratio = heat.Value / fuel;
        if (ratio > 1m)
        {
            Problem(ProblemSeverity.Warning, $"Die Wärmemenge des Warmwassers ({Num(heat.Value)} kWh) ist größer als die Brennstoffenergie ({Num(fuel)} kWh); der Anteil wird auf 100 % begrenzt.", HeatingCostCalculator.HotWaterKey);
            ratio = 1m;
        }

        return new Split(
            ratio,
            new ProtocolEntry(
                "Aufteilung Heizung/Warmwasser (§ 9 HeizkostenV)",
                Formula,
                $"{method}; Brennstoffenergie {Num(fuel)} kWh; {Num(heat.Value)} ÷ {Num(fuel)}",
                decimal.Round(ratio * 100m, 4),
                "Ergebnis in Prozent. Die Bezugsgröße (Heiz- oder Brennwert) von Brennstoffmenge und Formelfaktor muss zusammenpassen: bitte prüfen.")
            { Unit = "%" });
    }

    private decimal? HotWaterVolume()
    {
        decimal volume = 0;
        var missing = false;
        foreach (var unit in units)
        {
            var quantity = Consumption(unit.Id, MeterType.HotWater, billing);
            if (quantity.Value is { } value)
            {
                volume += value;
            }
            else
            {
                Problem(ProblemSeverity.Error, $"{unit.Name}: {quantity.Note ?? "Warmwasserverbrauch nicht ermittelbar."} Er wird für die Formel nach § 9 Abs. 2 HeizkostenV gebraucht.", HeatingCostCalculator.HotWaterKey);
                missing = true;
            }
        }

        return missing ? null : volume;
    }

    private void WarnAboutFlatRates()
    {
        foreach (var (leaseId, actual) in flatRateActuals.OrderBy(a => a.Key))
        {
            var lease = leases.First(l => l.Id == leaseId);
            Problem(
                ProblemSeverity.Warning,
                $"{lease.Label}: Die Heiz- und Warmwasserkosten werden nicht verbrauchsabhängig abgerechnet (Pauschale). Der Mieter darf seinen Kostenanteil um 15 % kürzen (§ 12 Abs. 1 HeizkostenV): bis zu {Money(CentDistribution.Cents(actual * 0.15m))} (15 % von {Money(actual)}). Bei zentraler Heizung ist eine Pauschale nur in Ausnahmefällen zulässig – rechtlich prüfen.",
                HeatingCostCalculator.HeatKey);
        }
    }
}
