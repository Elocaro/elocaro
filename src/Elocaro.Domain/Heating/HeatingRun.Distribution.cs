using Elocaro.Domain.Common;
using Elocaro.Domain.OperatingCosts;
using Elocaro.Domain.Receipts;

namespace Elocaro.Domain.Heating;

/// <summary>Distribution of a cost pool over the parties, CO₂ cost split and the summary sections.</summary>
internal sealed partial class HeatingRun
{
    private sealed class Outcome(string key, CostTypeResult result, decimal exactTotal)
    {
        public string Key { get; } = key;

        public CostTypeResult Result { get; } = result;

        public decimal ExactTotal { get; } = exactTotal;

        public Dictionary<Guid, TenantCostLine> Lines { get; } = [];

        /// <summary>Exact share (before rounding) of the leases that are billed and have agreed the cost type.</summary>
        public Dictionary<Guid, decimal> BilledExact { get; } = [];
    }

    private sealed class LineBuilder(string typeKey, BillingLease lease)
    {
        public string TypeKey { get; } = typeKey;

        public BillingLease Lease { get; } = lease;

        public decimal Exact { get; set; }

        public decimal Share { get; set; }

        public decimal NotAgreed { get; set; }

        public List<ProtocolEntry> Protocol { get; } = [];
    }

    private static string Describe(BillingCost cost) =>
        string.IsNullOrWhiteSpace(cost.Subtype) ? cost.Description : $"{cost.Subtype}: {cost.Description}";

    private bool IsFlatRateRoom(BillingLease lease) =>
        lease.RoomId is not null && unitsById[lease.UnitId] is { Rooms.Count: > 0, SharedFlat: SharedFlatBilling.FlatRate };

    private bool ReceivesStatement(BillingLease lease) => (lease.ReceivesHeatingStatement ?? lease.ReceivesStatement) && !IsFlatRateRoom(lease);

    private static bool Excluded(BillingLease lease, Pool target) =>
        lease.ExcludedCostTypes.Contains("betrkv-06")
        || (target == Pool.Heat ? lease.ExcludedCostTypes.Contains("betrkv-04") : lease.ExcludedCostTypes.Contains("betrkv-05"));

    private Outcome? Distribute(
        string name,
        string typeKey,
        List<Item> items,
        Pool target,
        Split split,
        decimal exact,
        decimal cents,
        List<Party> parties,
        Basis consumption,
        Basis area,
        string consumptionUnit,
        string consumptionName,
        string areaName)
    {
        if (exact == 0m && cents == 0m)
        {
            return null;
        }

        var percent = settings.ConsumptionPercent;
        var consumptionPart = exact * percent / 100m;
        var basePart = exact - consumptionPart;
        var sumConsumption = parties.Sum(p => p.Weight(consumption));
        var sumArea = parties.Sum(p => p.Weight(area));
        var itemLines = items
            .Where(i => Part(i, target, split.Ratio) != 0m)
            .Select(i =>
            {
                var part = Part(i, target, split.Ratio);
                var note = i.Pool == Pool.Joint ? $" × {Pct(target == Pool.Heat ? 1m - split.Ratio : split.Ratio)} ({(target == Pool.Heat ? "Heizung" : "Warmwasser")}, § 9)" : string.Empty;
                return new CostItemLine(i.Cost.Id, Describe(i.Cost), i.Cost.Amount, i.Cost.BillingPeriod, part, i.Calculation + note);
            })
            .ToList();
        var keyText = $"{Num(percent)} % {consumptionName}, {Num(100m - percent)} % {areaName}";

        if (sumConsumption <= 0m && sumArea <= 0m)
        {
            Problem(ProblemSeverity.Error, $"{name}: Es gibt weder Verbrauchs- noch Flächenwerte für die Verteilung; {Money(cents)} bleiben unverteilt beim Vermieter.", typeKey);
            var position = new LandlordPosition(typeKey, LandlordReason.Unassigned, "keine Verteilungsgrundlage", cents);
            return new Outcome(typeKey, new CostTypeResult(typeKey, name, CostGroup.Heating, cents, keyText, itemLines, 0m, cents, [position]), exact);
        }

        if (sumConsumption <= 0m)
        {
            Problem(ProblemSeverity.Error, $"{name}: Für keine Einheit liegt ein Verbrauch vor; die Verbrauchskosten werden vorläufig nach Fläche verteilt.", typeKey);
        }

        if (sumArea <= 0m)
        {
            Problem(ProblemSeverity.Error, $"{name}: Für keine Einheit liegt eine Fläche vor; die Grundkosten werden vorläufig nach Verbrauch verteilt.", typeKey);
        }

        decimal ExactShare(Party party)
        {
            var byConsumption = sumConsumption > 0 ? party.Weight(consumption) / sumConsumption : 0m;
            var byArea = sumArea > 0 ? party.Weight(area) / sumArea : 0m;
            return (consumptionPart * (sumConsumption > 0 ? byConsumption : byArea)) + (basePart * (sumArea > 0 ? byArea : byConsumption));
        }

        var exacts = parties.Select(ExactShare).ToList();
        var shares = CentDistribution.Distribute(cents, exacts);
        var builders = new Dictionary<Guid, LineBuilder>();
        var landlord = new List<LandlordPosition>();
        var costEntry = new ProtocolEntry(
            $"Kosten {name}",
            "Σ Betrag × Tage im Abrechnungszeitraum ÷ Tage der Position; bei der gemeinsamen Anlage × Anteil nach § 9",
            string.Join("; ", itemLines.Select(l => $"{l.Description}: {l.Calculation}")),
            cents);

        for (var i = 0; i < parties.Count; i++)
        {
            var party = parties[i];
            var share = shares[i];
            switch (party.Kind)
            {
                case PartyKind.Vacancy:
                    landlord.Add(new LandlordPosition(typeKey, LandlordReason.Vacancy, party.Label, share));
                    continue;
                case PartyKind.Unassigned:
                    landlord.Add(new LandlordPosition(typeKey, LandlordReason.Unassigned, party.Label, share));
                    continue;
            }

            var lease = party.Lease!;
            if (!ReceivesStatement(lease))
            {
                landlord.Add(new LandlordPosition(typeKey, IsFlatRateRoom(lease) ? LandlordReason.SharedFlatFlatRate : LandlordReason.FlatRate, lease.Label, share));
                flatRateActuals[lease.Id] = flatRateActuals.GetValueOrDefault(lease.Id) + share;
                continue;
            }

            var builder = new LineBuilder(typeKey, lease);
            builders[lease.Id] = builder;
            builder.Protocol.Add(costEntry);
            if (split.Entry is not null && items.Any(it => it.Pool == Pool.Joint && it.Prorated != 0m))
            {
                builder.Protocol.Add(split.Entry);
            }

            var consumptionWeight = party.Weight(consumption);
            var areaWeight = party.Weight(area);
            builder.Protocol.Add(new ProtocolEntry(
                $"Verbrauchsanteil {Num(percent)} % nach {consumptionName}",
                $"Kosten × Verbrauchsanteil × Verbrauch des Mieters ÷ Verbrauch aller Einheiten ({consumptionUnit})",
                $"{Money(exact)} × {Num(percent)} % = {Money(consumptionPart)} × {Num(consumptionWeight)} ÷ {Num(sumConsumption)} {consumptionUnit}",
                decimal.Round(sumConsumption > 0 ? consumptionPart * consumptionWeight / sumConsumption : 0m, 4, MidpointRounding.AwayFromZero),
                Describe(party, consumption, consumptionUnit)));
            builder.Protocol.Add(new ProtocolEntry(
                $"Grundkostenanteil {Num(100m - percent)} % nach {areaName}",
                $"Kosten × Grundkostenanteil × {areaName} × Tage des Mieters ÷ {areaName} × Tage aller Einheiten",
                $"{Money(exact)} × {Num(100m - percent)} % = {Money(basePart)} × {Num(areaWeight)} ÷ {Num(sumArea)} m²·Tage",
                decimal.Round(sumArea > 0 ? basePart * areaWeight / sumArea : 0m, 4, MidpointRounding.AwayFromZero),
                Describe(party, area, "m²·Tage")));

            if (Excluded(lease, target))
            {
                builder.NotAgreed = share;
                builder.Protocol.Add(new ProtocolEntry("Kostenart nicht vereinbart", "Anteil bleibt beim Vermieter", $"{Money(share)} nicht umlagefähig laut Mietvertrag", 0m));
                landlord.Add(new LandlordPosition(typeKey, LandlordReason.NotAgreed, lease.Label, share));
                continue;
            }

            builder.Exact = exacts[i];
            builder.Share = share;
            builder.Protocol.Add(new ProtocolEntry(
                "Rundung auf Cent (Restcent-Verteilung)",
                "Anteile werden auf Cent abgerundet; fehlende Cents erhalten die größten Nachkommastellen, damit die Summe aller Anteile genau den Kosten entspricht",
                $"{Money(exacts[i])} → {Money(share)}",
                share,
                share == CentDistribution.Cents(exacts[i]) ? null : $"Differenz {Money(share - CentDistribution.Cents(exacts[i]))} durch Restcent-Verteilung"));
        }

        var landlordAggregated = landlord
            .GroupBy(p => (p.Reason, p.Label))
            .Select(g => new LandlordPosition(typeKey, g.Key.Reason, g.Key.Label, g.Sum(p => p.Amount)))
            .Where(p => p.Amount != 0)
            .OrderBy(p => p.Reason).ThenBy(p => p.Label, StringComparer.Ordinal)
            .ToList();
        var tenantTotal = builders.Values.Sum(b => b.Share);
        var landlordTotal = landlordAggregated.Sum(p => p.Amount);
        if (tenantTotal + landlordTotal != cents)
        {
            throw new InvalidOperationException($"Verteilung von {name} geht nicht auf: {tenantTotal} + {landlordTotal} ≠ {cents}.");
        }

        var outcome = new Outcome(typeKey, new CostTypeResult(typeKey, name, CostGroup.Heating, cents, keyText, itemLines, tenantTotal, landlordTotal, landlordAggregated), exact);
        foreach (var builder in builders.Values)
        {
            var party = parties.First(p => p.Lease?.Id == builder.Lease.Id);
            outcome.Lines[builder.Lease.Id] = new TenantCostLine(
                typeKey,
                name,
                CostGroup.Heating,
                cents,
                keyText,
                $"{ShareText.Quantity(party.Weight(consumption), sumConsumption, consumptionUnit)}; {ShareText.PerDay(party.Weight(area), sumArea, billing.Days, "m²")}",
                builder.Share,
                builder.Exact,
                builder.NotAgreed,
                0m,
                0m,
                builder.Protocol);
            if (builder.NotAgreed == 0m)
            {
                outcome.BilledExact[builder.Lease.Id] = builder.Exact;
            }
        }

        return outcome;
    }

    // ---- CO₂ cost split (CO2KostAufG) ------------------------------------------------------------------------------

    private sealed class CarbonOutcome(CostTypeResult? result, Dictionary<Guid, TenantCostLine> lines, GroupSection? section)
    {
        public CostTypeResult? Result { get; } = result;

        public Dictionary<Guid, TenantCostLine> Lines { get; } = lines;

        public GroupSection? Section { get; } = section;
    }

    private decimal BuildingLivingArea()
    {
        decimal total = 0;
        foreach (var unit in units)
        {
            foreach (var state in unit.Areas)
            {
                total += state.LivingArea * state.Validity.DaysIn(billing);
            }
        }

        return total / billingDays;
    }

    private CarbonOutcome? Carbon(FuelResult fuel, Outcome? heat, Outcome? water, decimal carbonHeat, decimal carbonWater)
    {
        if (!settings.ApplyCarbonCosts || settings.Fuel == HeatingFuel.Other || CarbonCostTables.For(billing.From.Year) is not { } table)
        {
            return null;
        }

        var carbonCosts = carbonHeat + carbonWater;
        var co2 = fuel.Co2Kg;
        if (co2 <= 0m)
        {
            Problem(
                carbonCosts > 0 ? ProblemSeverity.Error : ProblemSeverity.Warning,
                carbonCosts > 0
                    ? "Die CO₂-Menge in kg fehlt auf dem Brennstoffbeleg. Ohne sie lässt sich die Stufe des CO2KostAufG nicht bestimmen; die CO₂-Kosten werden vorläufig voll den Mietern angerechnet."
                    : "Für die CO₂-Kostenaufteilung (CO2KostAufG) fehlen CO₂-Menge und CO₂-Kosten auf dem Brennstoffbeleg.",
                HeatingCostCalculator.CarbonKey);
            return null;
        }

        var livingArea = BuildingLivingArea();
        if (livingArea <= 0m)
        {
            Problem(ProblemSeverity.Error, "Für die CO₂-Einstufung fehlt die Wohnfläche des Gebäudes.", HeatingCostCalculator.CarbonKey);
            return null;
        }

        var classification = CarbonCostCalculator.Classify(table, co2, livingArea, billingDays);
        var level = classification.Level;
        var landlordFraction = level.LandlordPercent / 100m;
        if (!table.Verified)
        {
            Problem(ProblemSeverity.Warning, $"Stufentabelle des CO2KostAufG ({table.Version}) – {table.Status}.", HeatingCostCalculator.CarbonKey);
        }

        if (classification.YearFactor != 1m)
        {
            Problem(ProblemSeverity.Warning, $"Der Abrechnungszeitraum hat {billingDays} Tage; der CO₂-Ausstoß wird auf ein Jahr hochgerechnet (× {classification.YearFactor.ToString("0.####", German)}).", HeatingCostCalculator.CarbonKey);
        }

        foreach (var warning in ExceptionWarnings())
        {
            Problem(ProblemSeverity.Warning, warning, HeatingCostCalculator.CarbonKey);
        }

        if (carbonCosts == 0m)
        {
            Problem(ProblemSeverity.Warning, "Es sind keine CO₂-Kosten erfasst (Kostenposition mit der Art „CO₂-Kosten“ auf dem Brennstoffbeleg); die Aufteilung ist 0,00 €.", HeatingCostCalculator.CarbonKey);
        }

        // Exact deduction per billed lease: its share of the CO₂ costs in both pools times the landlord's share.
        var portionByLease = new Dictionary<Guid, decimal>();
        void Collect(Outcome? outcome, decimal carbon)
        {
            if (outcome is null || outcome.ExactTotal == 0m)
            {
                return;
            }

            foreach (var (leaseId, exact) in outcome.BilledExact)
            {
                portionByLease[leaseId] = portionByLease.GetValueOrDefault(leaseId) + (exact * carbon / outcome.ExactTotal);
            }
        }

        Collect(heat, carbonHeat);
        Collect(water, carbonWater);
        var exactByLease = portionByLease.ToDictionary(kv => kv.Key, kv => kv.Value * landlordFraction);
        var leaseIds = exactByLease.Keys.OrderBy(id => leases.First(l => l.Id == id).Term.From).ThenBy(id => id).ToList();
        var total = CentDistribution.Cents(leaseIds.Sum(id => exactByLease[id]));
        var deductions = CentDistribution.Distribute(total, leaseIds.Select(id => exactByLease[id]).ToList());
        var carbonCents = CentDistribution.Cents(carbonCosts);
        var landlordShareTotal = CentDistribution.Cents(carbonCosts * landlordFraction);
        var keyText = $"Stufe {level.Number}: Vermieter {Num(level.LandlordPercent)} %, Mieter {Num(level.TenantPercent)} %";

        var lines = new Dictionary<Guid, TenantCostLine>();
        for (var i = 0; i < leaseIds.Count; i++)
        {
            var lease = leases.First(l => l.Id == leaseIds[i]);
            var exact = exactByLease[leaseIds[i]];
            lines[lease.Id] = new TenantCostLine(
                HeatingCostCalculator.CarbonKey,
                "CO₂-Kosten: Vermieteranteil (Abzug)",
                CostGroup.Heating,
                landlordShareTotal,
                keyText,
                $"{Num(classification.SpecificKg)} kg CO₂/(m²·a) → {keyText}",
                -deductions[i],
                -exact,
                0m,
                0m,
                0m,
                [
                    new ProtocolEntry(
                        "CO₂-Kosten im Anteil des Mieters",
                        "Anteil an den Heiz- und Warmwasserkosten × CO₂-Kosten ÷ Kosten der Pools",
                        $"CO₂-Kosten {Money(carbonCosts)} (Heizung {Money(carbonHeat)}, Warmwasser {Money(carbonWater)})",
                        CentDistribution.Cents(portionByLease[leaseIds[i]])),
                    new ProtocolEntry(
                        "Einstufung nach CO2KostAufG",
                        $"kg CO₂ ÷ m² Wohnfläche ÷ Jahr, Stufentabelle {table.Version}",
                        $"{Num(co2)} kg ÷ {Num(livingArea)} m² = {classification.SpecificKg.ToString("#,##0.00", German)} kg/(m²·a) → {keyText}",
                        level.LandlordPercent,
                        table.Status),
                    new ProtocolEntry(
                        "Abzug des Vermieteranteils",
                        "Anteil des Mieters an den CO₂-Kosten × Vermieteranteil",
                        $"{Num(level.LandlordPercent)} %: {Money(exact)} → {Money(deductions[i])}",
                        -deductions[i],
                        "Der Abzug mindert die Heizkosten des Mieters; der Betrag trägt der Vermieter."),
                ]);
        }

        var tenantTotal = deductions.Sum();
        var position = new LandlordPosition(HeatingCostCalculator.CarbonKey, LandlordReason.CarbonCostShare, $"CO₂-Kostenanteil des Vermieters (Stufe {level.Number}, {Num(level.LandlordPercent)} %)", tenantTotal);
        var result = new CostTypeResult(
            HeatingCostCalculator.CarbonKey,
            "CO₂-Kostenaufteilung (Vermieteranteil)",
            CostGroup.Heating,
            0m,
            keyText,
            [],
            -tenantTotal,
            tenantTotal,
            tenantTotal == 0m ? [] : [position]);

        var section = new GroupSection(
            "carbon",
            "CO₂-Kostenaufteilung (CO2KostAufG)",
            [
                new ProtocolEntry("CO₂-Ausstoß", "Σ CO₂-Menge der Brennstoffrechnungen im Abrechnungszeitraum (Heizung und Warmwasser)", $"{Num(co2)} kg", co2),
                new ProtocolEntry("Wohnfläche des Gebäudes", "Σ Wohnfläche der Einheiten × Tage ÷ Tage im Abrechnungszeitraum", $"{Num(livingArea)} m²", livingArea),
                new ProtocolEntry(
                    "Spezifischer Ausstoß",
                    "kg CO₂ ÷ m² Wohnfläche (je Jahr)",
                    $"{Num(co2)} kg{(classification.YearFactor == 1m ? string.Empty : $" × {classification.YearFactor.ToString("0.####", German)}")} ÷ {Num(livingArea)} m²",
                    decimal.Round(classification.SpecificKg, 4, MidpointRounding.AwayFromZero),
                    classification.YearFactor == 1m
                        ? "kg/(m²·a), auf eine Nachkommastelle gerundet (§ 5 Abs. 1 Satz 3 CO2KostAufG)"
                        : "kg/(m²·a): der auf eine Nachkommastelle gerundete Wert des Zeitraums, auf ein Jahr umgerechnet; das entspricht der anteiligen Kürzung der Stufengrenzen (§ 5 Abs. 1 Satz 3 und 4 CO2KostAufG)"),
                new ProtocolEntry("Einstufung", $"Stufentabelle {table.Version}: {level.Range}", keyText, level.Number),
                new ProtocolEntry("CO₂-Kosten laut Lieferant", "Σ Kostenpositionen der Art „CO₂-Kosten“ im Zeitraum", $"Heizung {Money(carbonHeat)}, Warmwasser {Money(carbonWater)}", carbonCents),
                new ProtocolEntry("Vermieteranteil", "CO₂-Kosten × Vermieteranteil der Stufe", $"{Money(carbonCosts)} × {Num(level.LandlordPercent)} %", landlordShareTotal),
                new ProtocolEntry(
                    "Abzug bei den Mietern",
                    "Σ Abzüge der abgerechneten Mieter; der Rest betrifft Leerstand, Pauschalen und nicht vereinbarte Kosten, die der Vermieter ohnehin trägt",
                    $"Σ Mieter {Money(tenantTotal)}",
                    tenantTotal),
            ],
            $"Stufentabelle {table.Version}: {table.Status}");
        return carbonCosts == 0m ? new CarbonOutcome(null, [], section) : new CarbonOutcome(result, lines, section);
    }

    private IEnumerable<string> ExceptionWarnings()
    {
        switch (settings.CarbonSpecialCase)
        {
            case CarbonSpecialCase.ProtectedBuilding:
                yield return "Das Gebäude ist als denkmalgeschützt bzw. in einem Milieuschutzgebiet gekennzeichnet: Sonderregelungen des CO2KostAufG werden nicht berechnet, die Stufentabelle gilt unverändert. Bitte rechtlich prüfen.";
                break;
            case CarbonSpecialCase.HighlyEfficient:
                yield return "Das Gebäude ist als sehr energieeffizient gekennzeichnet (z. B. Passivhaus-Standard): Sonderregelungen werden nicht berechnet, die Stufentabelle gilt unverändert. Bitte prüfen.";
                break;
            case CarbonSpecialCase.NotSubject:
                yield return "Das Gebäude ist als möglicherweise nicht vom CO2KostAufG erfasst gekennzeichnet; die Aufteilung wird trotzdem berechnet. Bitte prüfen, ob das Gesetz anzuwenden ist.";
                break;
        }
    }

    // ---- summary ---------------------------------------------------------------------------------------------------

    private GroupSection SummarySection(List<Item> items, FuelResult fuel, Split split, decimal heatExact, decimal waterExact, decimal totalCents, Outcome? heat, Outcome? water)
    {
        var entries = new List<ProtocolEntry>();
        var kinds = items.GroupBy(i => i.Kind).OrderBy(g => g.Key)
            .Select(g => $"{KindName(g.Key)} {Money(CentDistribution.Cents(g.Sum(i => i.Prorated)))}");
        entries.Add(new ProtocolEntry(
            "Gesamtkosten der Heizanlage",
            "Σ Brennstoff, Betriebsstrom, Wartung, Abgasmessung, Reinigung, Zählermiete/Eichung, CO₂-Kosten (je × Tage im Zeitraum ÷ Tage der Position)",
            string.Join("; ", kinds),
            totalCents)
        { Unit = "€" });
        if (fuel.Kwh is { } kwh)
        {
            entries.Add(new ProtocolEntry("Brennstoffenergie", settings.Fuel == HeatingFuel.Oil ? "Anfangsbestand + Lieferungen − Endbestand" : "Σ Brennstoffmenge laut Beleg in kWh", fuel.Description, kwh) { Unit = "kWh" });
        }

        if (split.Entry is not null)
        {
            entries.Add(split.Entry);
        }

        entries.Add(new ProtocolEntry(
            "Kosten Heizung und Warmwasser",
            "Kosten nur Heizung + Kosten der gemeinsamen Anlage × (1 − Anteil Warmwasser); Warmwasser entsprechend",
            $"Heizung {Money(CentDistribution.Cents(heatExact))}, Warmwasser {Money(CentDistribution.Cents(waterExact))}",
            totalCents)
        { Unit = "€" });
        entries.Add(new ProtocolEntry(
            "Verteilungsschlüssel",
            "§ 7 Abs. 1 und § 8 Abs. 1 HeizkostenV: 50 bis 70 % nach Verbrauch, der Rest nach Fläche",
            $"{Num(settings.ConsumptionPercent)} % Verbrauch, {Num(100m - settings.ConsumptionPercent)} % Fläche (Heizung: Wärmemenge und Heizfläche; Warmwasser: Warmwasser-m³ und Wohnfläche)",
            null));
        if (heat is null && water is null)
        {
            entries.Add(new ProtocolEntry("Keine Kosten", "-", "Im Zeitraum fallen keine Heizkosten an", null));
        }

        return new GroupSection("heating", "Heiz- und Warmwasserkosten (HeizkostenV)", entries);
    }
}
