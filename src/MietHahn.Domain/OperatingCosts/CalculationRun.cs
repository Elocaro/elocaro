using System.Globalization;
using MietHahn.Domain.Common;
using MietHahn.Domain.Leases;
using MietHahn.Domain.Metering;
using MietHahn.Domain.Receipts;

namespace MietHahn.Domain.OperatingCosts;

/// <summary>One run of the calculation; holds the intermediate state (problems, shares per lease).</summary>
internal sealed class CalculationRun
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    private readonly BillingInput input;
    private readonly Period billing;
    private readonly DateOnly billingEnd;
    private readonly int billingDays;
    private readonly List<ICostGroupCalculator> calculators;
    private readonly List<CalculationProblem> problems = [];
    private readonly HashSet<string> problemKeys = [];
    private readonly int vacancyPersons;
    private readonly List<BillingUnit> units;
    private readonly Dictionary<Guid, BillingUnit> unitsById;
    private readonly List<BillingLease> leases;
    private readonly Dictionary<Guid, BillingLease> leasesById;
    private readonly SortedSet<DateOnly> breakpoints = [];
    private readonly Dictionary<(Guid, MeterType, Period), ConsumptionQuantity> consumptionCache = [];

    private readonly List<CostTypeResult> costTypes = [];
    private readonly List<LandlordPosition> landlord = [];
    private readonly Dictionary<Guid, List<TenantCostLine>> linesByLease = [];
    private readonly Dictionary<Guid, decimal> actualByLease = [];
    private readonly List<GroupSection> sections = [];

    public CalculationRun(BillingInput input, List<ICostGroupCalculator> calculators)
    {
        this.input = input;
        this.calculators = calculators;
        billing = input.BillingPeriod;
        billingEnd = billing.To ?? throw new RuleViolationException("Der Abrechnungszeitraum braucht ein Ende.");
        if (billingEnd > billing.From.AddYears(1).AddDays(-1))
        {
            throw new RuleViolationException("Der Abrechnungszeitraum darf höchstens 12 Monate umfassen (§ 556 Abs. 3 BGB).");
        }

        billingDays = billing.Days;
        vacancyPersons = Math.Max(0, input.Options?.VacancyPersons ?? 1);
        units = [.. input.Units.OrderBy(u => u.Name, StringComparer.Ordinal).ThenBy(u => u.Id)];
        unitsById = units.ToDictionary(u => u.Id);
        leases = [];
        foreach (var lease in input.Leases.Where(l => l.Term.Overlaps(billing)).OrderBy(l => l.Term.From).ThenBy(l => l.Id))
        {
            if (!unitsById.TryGetValue(lease.UnitId, out var unit))
            {
                Problem(ProblemSeverity.Warning, $"Der Vertrag „{lease.Label}“ gehört zu einer Einheit, die nicht an der Abrechnung teilnimmt, und wird übersprungen.");
            }
            else if (unit.Rooms.Count > 0 && unit.Rooms.All(r => r.Id != lease.RoomId))
            {
                Problem(ProblemSeverity.Warning, $"Der Vertrag „{lease.Label}“ ist keinem Zimmer der Wohngemeinschaft {unit.Name} zugeordnet und wird übersprungen.");
            }
            else
            {
                leases.Add(lease);
            }
        }

        leasesById = leases.ToDictionary(l => l.Id);
        CollectBreakpoints();
    }

    private sealed record CostPart(BillingCost Cost, decimal Prorated, string Calculation);

    private sealed record RuleSlice(Period Span, BillingRule? Rule);

    private sealed record Piece(RuleSlice Slice, BillingRule Rule, Guid? DirectUnit, IReadOnlyList<CostPart> Parts, decimal Exact);

    private enum PartyKind
    {
        Lease,
        Vacancy,
        Residual,
        Unassigned,
    }

    private sealed record Part(Period Span, string Basis, bool PerDay, int Days, decimal Value, bool Interpolated);

    private sealed class Party(string key, PartyKind kind, Guid? leaseId, string label, string sortKey)
    {
        public string Key { get; } = key;

        public PartyKind Kind { get; } = kind;

        public Guid? LeaseId { get; } = leaseId;

        public string Label { get; } = label;

        public string SortKey { get; } = sortKey;

        public decimal Weight { get; set; }

        public List<Part> Parts { get; } = [];
    }

    private sealed class WeightSet(string unit)
    {
        public string Unit { get; } = unit;

        public List<Party> Parties { get; } = [];

        public decimal Total => Parties.Sum(p => p.Weight);

        public Party Get(string key, PartyKind kind, Guid? leaseId, string label, string sortKey)
        {
            var party = Parties.FirstOrDefault(p => p.Key == key);
            if (party is null)
            {
                party = new Party(key, kind, leaseId, label, sortKey);
                Parties.Add(party);
            }

            return party;
        }
    }

    private sealed class LineBuilder(BillingLease lease, string costType, string name, decimal costTotal)
    {
        public BillingLease Lease { get; } = lease;

        public string CostType { get; } = costType;

        public string Name { get; } = name;

        public decimal CostTotal { get; } = costTotal;

        public decimal Share { get; set; }

        public decimal Exact { get; set; }

        public decimal NotAgreed { get; set; }

        public List<ProtocolEntry> Protocol { get; } = [];

        public List<string> Keys { get; } = [];

        public List<string> Bases { get; } = [];
    }

    public BillingResult Execute()
    {
        var parts = new List<CostPart>();
        var nonAllocable = new List<CostItemLine>();
        foreach (var cost in input.Costs.OrderBy(c => c.BillingPeriod.From).ThenBy(c => c.Id))
        {
            if (cost.BillingPeriod.IsOpenEnded)
            {
                Problem(ProblemSeverity.Error, $"Die Kostenposition „{cost.Description}“ hat keinen abgeschlossenen Abrechnungszeitraum und wird nicht berücksichtigt.", cost.CostType);
                continue;
            }

            var overlap = cost.BillingPeriod.DaysIn(billing);
            if (overlap == 0)
            {
                continue;
            }

            var prorated = cost.Amount * overlap / cost.BillingPeriod.Days;
            var calculation = overlap == cost.BillingPeriod.Days
                ? $"{Money(cost.Amount)} (vollständig im Zeitraum)"
                : $"{Money(cost.Amount)} × {overlap}/{cost.BillingPeriod.Days} Tage";
            if (!cost.Allocable)
            {
                nonAllocable.Add(new CostItemLine(cost.Id, cost.Description, cost.Amount, cost.BillingPeriod, prorated, calculation));
                continue;
            }

            parts.Add(new CostPart(cost, prorated, calculation));
        }

        var operating = parts.Where(p => CostGroups.Of(p.Cost.CostType) == CostGroup.Operating).ToList();
        foreach (var type in OrderedTypes(operating.Select(p => p.Cost.CostType)))
        {
            CalculateType(type, operating.Where(p => p.Cost.CostType == type).ToList());
        }

        var uncalculated = new List<UncalculatedGroup>();
        var heating = parts.Where(p => CostGroups.Of(p.Cost.CostType) == CostGroup.Heating).ToList();
        if (heating.Count > 0)
        {
            var calculator = calculators.FirstOrDefault(c => c.Group == CostGroup.Heating);
            if (calculator is not null)
            {
                MergeGroup(calculator.Calculate(input, [.. heating.Select(p => p.Cost)]));
            }
            else
            {
                var lines = heating.Select(Line).ToList();
                uncalculated.Add(new UncalculatedGroup(
                    CostGroup.Heating,
                    "Heiz- und Warmwasserkosten (Abrechnung nach HeizkostenV)",
                    CentDistribution.Cents(lines.Sum(l => l.Prorated)),
                    lines));
            }
        }

        var statements = new List<TenantStatement>();
        var flatRates = new List<FlatRateComparison>();
        foreach (var lease in leases)
        {
            if (StatementFor(lease))
            {
                statements.Add(Statement(lease));
            }

            if (!OperatingStatementFor(lease))
            {
                flatRates.Add(FlatRate(lease));
            }
        }

        return new BillingResult(
            billing,
            costTypes,
            statements,
            flatRates,
            landlord,
            nonAllocable,
            uncalculated,
            problems,
            sections);
    }

    private static string Money(decimal value) => value.ToString("#,##0.00 €", German);

    private static string Num(decimal value) => value.ToString("#,##0.###", German);

    private static string Day(DateOnly value) => value.ToString("dd.MM.yyyy", German);

    private static string Span(Period period) => period.To is { } to ? $"{Day(period.From)} – {Day(to)}" : $"ab {Day(period.From)}";

    private static CostItemLine Line(CostPart part) =>
        new(part.Cost.Id, Describe(part.Cost), part.Cost.Amount, part.Cost.BillingPeriod, part.Prorated, part.Calculation);

    private static string Describe(BillingCost cost) =>
        string.IsNullOrWhiteSpace(cost.Subtype) ? cost.Description : $"{cost.Subtype}: {cost.Description}";

    private static IEnumerable<string> OrderedTypes(IEnumerable<string> keys) =>
        keys.Distinct(StringComparer.Ordinal)
            .OrderBy(k => CostTypes.All.ToList().FindIndex(t => t.Key == k) is var i and >= 0 ? i : int.MaxValue)
            .ThenBy(k => k, StringComparer.Ordinal);

    private static string TypeName(string key) => CostTypes.Find(key)?.Name ?? key;

    private void Problem(ProblemSeverity severity, string text, string? costType = null)
    {
        if (problemKeys.Add($"{severity}|{costType}|{text}"))
        {
            problems.Add(new CalculationProblem(severity, text, costType));
        }
    }

    /// <summary>The lease gets an operating cost statement (prepayment lease, not a flat-rate room).</summary>
    private bool OperatingStatementFor(BillingLease lease) =>
        lease.ReceivesStatement && !IsFlatRateRoom(lease);

    /// <summary>Operating costs and/or heating costs are billed to the lease (heating by consumption despite a flat rate).</summary>
    private bool StatementFor(BillingLease lease) =>
        OperatingStatementFor(lease)
        || (lease.ReceivesHeatingStatement == true && !IsFlatRateRoom(lease) && (linesByLease.GetValueOrDefault(lease.Id)?.Any(l => l.Group == CostGroup.Heating) ?? false));

    private bool IsFlatRateRoom(BillingLease lease) =>
        lease.RoomId is not null && unitsById[lease.UnitId] is { Rooms.Count: > 0, SharedFlat: SharedFlatBilling.FlatRate };

    private void CollectBreakpoints()
    {
        void Add(Period period)
        {
            breakpoints.Add(period.From);
            if (period.To is { } to)
            {
                breakpoints.Add(to.AddDays(1));
            }
        }

        foreach (var lease in leases)
        {
            Add(lease.Term);
            lease.Persons.ToList().ForEach(p => Add(p.Validity));
        }

        foreach (var unit in units)
        {
            unit.Areas.ToList().ForEach(a => Add(a.Validity));
            foreach (var room in unit.Rooms)
            {
                room.Areas.ToList().ForEach(a => Add(a.Validity));
            }
        }
    }

    private List<Period> Segments(Period span)
    {
        var to = span.To!.Value;
        var points = new SortedSet<DateOnly> { span.From };
        foreach (var point in breakpoints.Where(b => b > span.From && b <= to))
        {
            points.Add(point);
        }

        var list = points.ToList();
        var result = new List<Period>();
        for (var i = 0; i < list.Count; i++)
        {
            var end = i + 1 < list.Count ? list[i + 1].AddDays(-1) : to;
            result.Add(new Period(list[i], end));
        }

        return result;
    }

    private BillingLease? LeaseAt(Guid unitId, Guid? roomId, DateOnly day) =>
        leases.FirstOrDefault(l => l.UnitId == unitId && (roomId is null || l.CoversRoom(roomId.Value)) && l.Term.Contains(day));

    private List<RuleSlice> Slices(string type)
    {
        var rules = input.Rules.Where(r => r.CostType == type && r.Validity.Overlaps(billing)).OrderBy(r => r.Validity.From).ToList();
        var points = new SortedSet<DateOnly> { billing.From };
        foreach (var rule in rules)
        {
            if (rule.Validity.From > billing.From && rule.Validity.From <= billingEnd)
            {
                points.Add(rule.Validity.From);
            }

            if (rule.Validity.To is { } to && to < billingEnd)
            {
                points.Add(to.AddDays(1));
            }
        }

        var list = points.ToList();
        var slices = new List<RuleSlice>();
        for (var i = 0; i < list.Count; i++)
        {
            var end = i + 1 < list.Count ? list[i + 1].AddDays(-1) : billingEnd;
            var rule = rules.FirstOrDefault(r => r.Validity.Contains(list[i]));
            var span = new Period(list[i], end);
            if (slices.Count > 0 && ReferenceEquals(slices[^1].Rule, rule))
            {
                slices[^1] = slices[^1] with { Span = new Period(slices[^1].Span.From, end) };
            }
            else
            {
                slices.Add(new RuleSlice(span, rule));
            }
        }

        return slices;
    }

    private static string KeyLabel(BillingRule rule) => rule.Key switch
    {
        AllocationKey.LivingArea => "Wohnfläche",
        AllocationKey.HeatedArea => "Heizfläche",
        AllocationKey.Persons => "Personen (Personentage)",
        AllocationKey.Units => "Einheiten",
        AllocationKey.Consumption => $"Verbrauch ({MeterTypeLabel(rule.MeterType)})",
        AllocationKey.Direct => "direkte Zuordnung",
        _ => "fester Prozentsatz",
    };

    private static string MeterTypeLabel(MeterType? type) => type switch
    {
        MeterType.ColdWater => "Kaltwasser",
        MeterType.HotWater => "Warmwasser",
        MeterType.Heat => "Wärme",
        MeterType.Electricity => "Strom",
        MeterType.Gas => "Gas",
        MeterType.OilTank => "Öl",
        _ => "kein Zählertyp",
    };

    private static string Formula(BillingRule rule) => rule.Key switch
    {
        AllocationKey.LivingArea => "Kosten × Σ(Wohnfläche × Tage) des Mieters ÷ Σ(Wohnfläche × Tage) aller Einheiten",
        AllocationKey.HeatedArea => "Kosten × Σ(Heizfläche × Tage) des Mieters ÷ Σ(Heizfläche × Tage) aller Einheiten",
        AllocationKey.Persons => "Kosten × Personentage des Mieters ÷ Personentage aller Einheiten",
        AllocationKey.Units => "Kosten × Einheitentage des Mieters ÷ Einheitentage aller Einheiten",
        AllocationKey.Consumption => "Kosten × Verbrauch des Mieters ÷ Verbrauch aller Einheiten",
        AllocationKey.Direct => "Kosten × Tage des Mieters ÷ Tage der Einheit im Zeitraum",
        _ => "Kosten × Prozentsatz der Einheit × Tage des Mieters ÷ (100 % × Tage im Zeitraum)",
    };

    private static string WeightUnit(BillingRule rule) => rule.Key switch
    {
        AllocationKey.LivingArea => "m²·Tage",
        AllocationKey.HeatedArea => "m²·Tage",
        AllocationKey.Persons => "Personentage",
        AllocationKey.Units or AllocationKey.Direct => "Einheitentage",
        AllocationKey.Consumption => "Verbrauchseinheiten",
        _ => "%·Tage",
    };

    // ---- weights ---------------------------------------------------------------------------------------------------

    private ConsumptionQuantity Consumption(Guid unitId, MeterType type, Period period)
    {
        if (!consumptionCache.TryGetValue((unitId, type, period), out var quantity))
        {
            quantity = input.Consumption.Quantity(unitId, type, period);
            consumptionCache[(unitId, type, period)] = quantity;
        }

        return quantity;
    }

    private int PersonsOn(BillingLease lease, DateOnly day)
    {
        var state = lease.Persons.FirstOrDefault(p => p.Validity.Contains(day));
        if (state is null)
        {
            Problem(ProblemSeverity.Error, $"Für den Vertrag „{lease.Label}“ ist am {Day(day)} keine Personenanzahl hinterlegt (angenommen: 1 Person).");
            return 1;
        }

        return state.Count;
    }

    private static void AddPart(Party party, Part part)
    {
        party.Weight += part.Value;
        if (party.Parts.Count > 0)
        {
            var last = party.Parts[^1];
            if (last.Basis == part.Basis && last.PerDay == part.PerDay && last.Span.To!.Value.AddDays(1) == part.Span.From)
            {
                party.Parts[^1] = last with
                {
                    Span = new Period(last.Span.From, part.Span.To),
                    Days = last.Days + part.Days,
                    Value = last.Value + part.Value,
                    Interpolated = last.Interpolated || part.Interpolated,
                };
                return;
            }
        }

        party.Parts.Add(part);
    }

    private static Party OccupantParty(WeightSet set, BillingUnit unit, BillingRoom? room, BillingLease? lease)
    {
        if (lease is not null)
        {
            return set.Get($"L:{lease.Id}", PartyKind.Lease, lease.Id, lease.Label, $"0|{lease.Term.From:yyyyMMdd}|{lease.Id}");
        }

        var subject = room?.Id ?? unit.Id;
        var label = room is null ? $"Leerstand {unit.Name}" : $"Leerstand {unit.Name}, {room.Name}";
        return set.Get($"V:{subject}", PartyKind.Vacancy, null, label, $"1|{label}|{subject}");
    }

    private WeightSet BuildWeights(Period span, BillingRule rule, Guid? directUnit)
    {
        var set = new WeightSet(WeightUnit(rule));
        var segments = Segments(span);
        foreach (var unit in units)
        {
            if (unit.SelfPaidCostTypes?.Contains(rule.CostType) == true)
            {
                continue;
            }

            if ((rule.Key == AllocationKey.Direct && unit.Id != directUnit)
                || (rule.Key == AllocationKey.FixedPercent && rule.Shares?.All(s => s.UnitId != unit.Id) != false))
            {
                continue;
            }

            foreach (var segment in segments)
            {
                AddUnit(set, unit, segment, rule);
            }
        }

        if (rule.Key == AllocationKey.FixedPercent)
        {
            var rest = (100m * span.Days) - set.Parties.Sum(p => p.Weight);
            if (rest > 0)
            {
                var residual = set.Get("R", PartyKind.Residual, null, "nicht verteilter Prozentanteil", "2|R");
                residual.Weight += rest;
                residual.Parts.Add(new Part(span, $"{Num(rest / span.Days)} %", true, span.Days, rest, false));
            }
        }

        set.Parties.Sort((a, b) => string.CompareOrdinal(a.SortKey, b.SortKey));
        return set;
    }

    private void AddUnit(WeightSet set, BillingUnit unit, Period segment, BillingRule rule)
    {
        var day = segment.From;
        var days = segment.Days;
        if (unit.Rooms.Count == 0)
        {
            var lease = LeaseAt(unit.Id, null, day);
            Part part;
            if (rule.Key == AllocationKey.Persons)
            {
                var persons = lease is null ? vacancyPersons : PersonsOn(lease, day);
                part = new Part(segment, $"{persons} {(persons == 1 ? "Person" : "Personen")}", true, days, persons * days, false);
            }
            else
            {
                part = UnitPart(unit, segment, rule);
            }

            AddPart(OccupantParty(set, unit, null, lease), part);
            return;
        }

        var rooms = unit.Rooms.OrderBy(r => r.Name, StringComparer.Ordinal).ThenBy(r => r.Id).ToList();
        if (rule.Key == AllocationKey.Persons)
        {
            var counted = new HashSet<Guid>();
            foreach (var room in rooms)
            {
                var lease = LeaseAt(unit.Id, room.Id, day);
                if (lease is not null && !counted.Add(lease.Id))
                {
                    continue; // a lease on several rooms counts its persons once
                }

                var persons = lease is null ? vacancyPersons : PersonsOn(lease, day);
                AddPart(
                    OccupantParty(set, unit, room, lease),
                    new Part(segment, $"{persons} {(persons == 1 ? "Person" : "Personen")}", true, days, persons * days, false));
            }

            return;
        }

        var unitPart = UnitPart(unit, segment, rule);
        var areas = rooms.Select(r => r.Areas.FirstOrDefault(a => a.Validity.Contains(day))?.AllocationArea ?? 0m).ToList();

        // Metered consumption was caused by the occupants: a vacant room takes none of it (as long as any room is rented).
        // Area-based shares (property tax, insurance ...) keep the vacant rooms, the landlord bears them.
        var occupied = rooms.Select(r => LeaseAt(unit.Id, r.Id, day) is not null).ToList();
        if (rule.Key == AllocationKey.Consumption && occupied.Any(o => o))
        {
            areas = [.. areas.Select((a, i) => occupied[i] ? a : 0m)];
        }

        var areaTotal = areas.Sum();
        if (areaTotal <= 0)
        {
            Problem(ProblemSeverity.Error, $"Für die Zimmer der Wohngemeinschaft {unit.Name} ist am {Day(day)} keine Fläche hinterlegt; der Anteil der Wohnung kann nicht auf die Zimmer verteilt werden.");
            var unassigned = set.Get("U", PartyKind.Unassigned, null, $"nicht verteilbar ({unit.Name})", "3|U");
            AddPart(unassigned, unitPart);
            return;
        }

        for (var i = 0; i < rooms.Count; i++)
        {
            if (areas[i] == 0m && rule.Key == AllocationKey.Consumption && !occupied[i])
            {
                continue;
            }

            var lease = LeaseAt(unit.Id, rooms[i].Id, day);
            var share = areas[i] / areaTotal;
            AddPart(
                OccupantParty(set, unit, rooms[i], lease),
                unitPart with
                {
                    Basis = $"{unitPart.Basis}, Zimmeranteil {Num(areas[i])} von {Num(areaTotal)} m²",
                    Value = unitPart.Value * share,
                });
        }
    }

    private Part UnitPart(BillingUnit unit, Period segment, BillingRule rule)
    {
        var day = segment.From;
        var days = segment.Days;
        switch (rule.Key)
        {
            case AllocationKey.LivingArea:
            case AllocationKey.HeatedArea:
                var state = unit.Areas.FirstOrDefault(a => a.Validity.Contains(day));
                if (state is null)
                {
                    Problem(ProblemSeverity.Error, $"Für {unit.Name} ist am {Day(day)} keine Fläche hinterlegt.", rule.CostType);
                    return new Part(segment, "keine Fläche", true, days, 0m, false);
                }

                var area = rule.Key == AllocationKey.LivingArea ? state.LivingArea : state.HeatedArea;
                return new Part(segment, $"{Num(area)} m²", true, days, area * days, false);
            case AllocationKey.Consumption:
                if (rule.MeterType is not { } type)
                {
                    Problem(ProblemSeverity.Error, $"Für die Kostenart {TypeName(rule.CostType)} ist beim Schlüssel „Verbrauch“ kein Zählertyp gewählt.", rule.CostType);
                    return new Part(segment, "Verbrauch", false, days, 0m, false);
                }

                var quantity = Consumption(unit.Id, type, segment);
                if (quantity.Value is not { } value)
                {
                    Problem(ProblemSeverity.Error, $"{unit.Name}: {quantity.Note ?? "Verbrauch nicht ermittelbar."}", rule.CostType);
                    return new Part(segment, "Verbrauch fehlt", false, days, 0m, false);
                }

                return new Part(segment, "Verbrauch", false, days, value, quantity.Interpolated);
            case AllocationKey.FixedPercent:
                var percent = rule.Shares?.FirstOrDefault(s => s.UnitId == unit.Id)?.Percent ?? 0m;
                return new Part(segment, $"{Num(percent)} %", true, days, percent * days, false);
            default:
                return new Part(segment, "1 Einheit", true, days, days, false);
        }
    }

    private static string DescribeParts(Party party, string unit) =>
        string.Join("; ", party.Parts.Select(p => p.PerDay
            ? $"{Span(p.Span)}: {p.Basis} × {p.Days} Tage = {Num(p.Value)} {unit}"
            : $"{Span(p.Span)}: {Num(p.Value)} {unit}{(p.Interpolated ? " (interpolierter Zählerstand)" : "")}"));

    // ---- cost type -------------------------------------------------------------------------------------------------

    private void CalculateType(string type, List<CostPart> parts)
    {
        var name = TypeName(type);
        var itemLines = parts.Select(Line).ToList();
        var slices = Slices(type);
        if (slices.Any(s => s.Rule is null))
        {
            Problem(ProblemSeverity.Warning, $"Für die Kostenart {name} ist nicht für den ganzen Zeitraum ein Umlageschlüssel hinterlegt (Standard: Wohnfläche).", type);
        }

        var pieces = new List<Piece>();
        foreach (var slice in slices)
        {
            var rule = slice.Rule ?? new BillingRule(type, slice.Span, AllocationKey.LivingArea);
            var inSlice = parts.Where(p => p.Cost.BillingPeriod.Overlaps(slice.Span)).ToList();
            if (inSlice.Count == 0)
            {
                continue;
            }

            if (rule.Key == AllocationKey.Direct)
            {
                foreach (var group in inSlice.GroupBy(p => p.Cost.UnitId).OrderBy(g => g.Key is null ? int.MaxValue : units.FindIndex(u => u.Id == g.Key)))
                {
                    pieces.Add(NewPiece(slice, rule, group.Key, [.. group]));
                }
            }
            else
            {
                if (inSlice.Any(p => p.Cost.UnitId is not null))
                {
                    Problem(ProblemSeverity.Warning, $"Kostenart {name}: Die Einheit einer Kostenposition wird beim Schlüssel „{KeyLabel(rule)}“ nicht beachtet.", type);
                }

                pieces.Add(NewPiece(slice, rule, null, inSlice));
            }
        }

        var exactTotal = pieces.Sum(p => p.Exact);
        var total = CentDistribution.Cents(exactTotal);
        var pieceCents = CentDistribution.Distribute(total, pieces.Select(p => p.Exact).ToList());
        var builders = new Dictionary<Guid, LineBuilder>();
        var landlordHere = new List<LandlordPosition>();
        var keyTexts = new List<string>();
        var labor = (
            Household: parts.Where(p => p.Cost.Section35a == Section35a.HouseholdService).Sum(p => Labor(p)),
            Tradesman: parts.Where(p => p.Cost.Section35a == Section35a.TradesmanService).Sum(p => Labor(p)));

        for (var i = 0; i < pieces.Count; i++)
        {
            var piece = pieces[i];
            ProcessPiece(type, name, total, piece, pieceCents[i], pieces.Count > 1, itemLines, builders, landlordHere);
            var keyText = KeyLabel(piece.Rule);
            if (!keyTexts.Contains(keyText) || slices.Count > 1)
            {
                keyTexts.Add(slices.Count > 1 ? $"{keyText} ({Span(piece.Slice.Span)})" : keyText);
            }
        }

        var key = string.Join(", ", keyTexts.Distinct());
        var landlordAggregated = landlordHere
            .GroupBy(p => (p.Reason, p.Label))
            .Select(g => new LandlordPosition(type, g.Key.Reason, g.Key.Label, g.Sum(p => p.Amount)))
            .Where(p => p.Amount != 0)
            .OrderBy(p => p.Reason).ThenBy(p => p.Label, StringComparer.Ordinal)
            .ToList();
        landlord.AddRange(landlordAggregated);

        var tenantTotal = 0m;
        foreach (var builder in builders.Values.OrderBy(b => b.Lease.Term.From).ThenBy(b => b.Lease.Id))
        {
            var household = total == 0 ? 0m : CentDistribution.Cents(labor.Household * builder.Share / total);
            var tradesman = total == 0 ? 0m : CentDistribution.Cents(labor.Tradesman * builder.Share / total);
            var line = new TenantCostLine(
                type, name, CostGroup.Operating, total, string.Join(", ", builder.Keys.Distinct()), string.Join("; ", builder.Bases),
                builder.Share, builder.Exact, builder.NotAgreed, household, tradesman, builder.Protocol);
            if (!linesByLease.TryGetValue(builder.Lease.Id, out var lines))
            {
                lines = [];
                linesByLease[builder.Lease.Id] = lines;
            }

            lines.Add(line);
            tenantTotal += builder.Share;
        }

        var landlordTotal = landlordAggregated.Sum(p => p.Amount);
        if (tenantTotal + landlordTotal != total)
        {
            throw new InvalidOperationException($"Verteilung der Kostenart {name} geht nicht auf: {tenantTotal} + {landlordTotal} ≠ {total}.");
        }

        costTypes.Add(new CostTypeResult(type, name, CostGroup.Operating, total, key, itemLines, tenantTotal, landlordTotal, landlordAggregated));
    }

    private static decimal Labor(CostPart part) =>
        (part.Cost.LaborCosts ?? 0m) * (part.Prorated == 0m || part.Cost.Amount == 0m ? 0m : part.Prorated / part.Cost.Amount);

    private static Piece NewPiece(RuleSlice slice, BillingRule rule, Guid? directUnit, List<CostPart> parts)
    {
        var exact = parts.Sum(p => p.Cost.Amount * p.Cost.BillingPeriod.DaysIn(slice.Span) / p.Cost.BillingPeriod.Days);
        return new Piece(slice, rule, directUnit, parts, exact);
    }

    private void ProcessPiece(
        string type,
        string name,
        decimal typeTotal,
        Piece piece,
        decimal cents,
        bool multiple,
        List<CostItemLine> itemLines,
        Dictionary<Guid, LineBuilder> builders,
        List<LandlordPosition> landlordHere)
    {
        var rule = piece.Rule;
        var span = piece.Slice.Span;
        var suffix = multiple ? $" ({Span(span)}{(piece.DirectUnit is { } du && unitsById.TryGetValue(du, out var duUnit) ? ", " + duUnit.Name : string.Empty)})" : string.Empty;

        // Deduction before allocation
        var deduction = 0m;
        if (rule.PreDeductionPercent is { } percent)
        {
            deduction = CentDistribution.Cents(cents * percent / 100m);
        }
        else if (rule.PreDeductionAmount is { } fixedAmount)
        {
            deduction = Math.Min(CentDistribution.Cents(fixedAmount * span.Days / billingDays), Math.Abs(cents)) * Math.Sign(cents);
        }

        var net = cents - deduction;
        if (deduction != 0)
        {
            landlordHere.Add(new LandlordPosition(type, LandlordReason.PreDeduction, rule.PreDeductionNote ?? "Vorwegabzug", deduction));
        }

        var set = BuildWeights(span, rule, piece.DirectUnit);
        var weightTotal = set.Total;
        if (set.Parties.Count == 0 || weightTotal <= 0)
        {
            Problem(
                ProblemSeverity.Error,
                $"Kostenart {name}{suffix}: Es gibt keine Verteilungsgrundlage ({KeyLabel(rule)}); {Money(net)} bleiben unverteilt beim Vermieter.",
                type);
            landlordHere.Add(new LandlordPosition(type, LandlordReason.Unassigned, "keine Verteilungsgrundlage", net));
            return;
        }

        var shares = CentDistribution.Distribute(net, set.Parties.Select(p => p.Weight).ToList());
        var costEntry = new ProtocolEntry(
            $"Gesamtkosten {name}",
            "Σ Betrag × Tage im Abrechnungszeitraum ÷ Tage der Position",
            string.Join("; ", itemLines.Select(l => $"{l.Description}: {l.Calculation}")),
            typeTotal);
        var unitLabel = WeightUnit(rule);

        for (var i = 0; i < set.Parties.Count; i++)
        {
            var party = set.Parties[i];
            var share = shares[i];
            var exact = net * party.Weight / weightTotal;
            switch (party.Kind)
            {
                case PartyKind.Vacancy:
                    landlordHere.Add(new LandlordPosition(type, LandlordReason.Vacancy, party.Label, share));
                    continue;
                case PartyKind.Residual:
                    landlordHere.Add(new LandlordPosition(type, LandlordReason.ResidualPercent, party.Label, share));
                    continue;
                case PartyKind.Unassigned:
                    landlordHere.Add(new LandlordPosition(type, LandlordReason.Unassigned, party.Label, share));
                    continue;
            }

            var lease = leasesById[party.LeaseId!.Value];
            if (!builders.TryGetValue(lease.Id, out var builder))
            {
                builder = new LineBuilder(lease, type, name, typeTotal);
                builder.Protocol.Add(costEntry);
                builders[lease.Id] = builder;
            }

            builder.Keys.Add(KeyLabel(rule));
            builder.Bases.Add($"{Num(party.Weight)} von {Num(weightTotal)} {unitLabel}");
            builder.Protocol.Add(new ProtocolEntry(
                $"Anteil {name}{suffix}",
                Formula(rule),
                $"{Money(net)}{(deduction != 0 ? $" (nach Vorwegabzug von {Money(deduction)})" : string.Empty)} × {Num(party.Weight)} ÷ {Num(weightTotal)} {unitLabel}",
                decimal.Round(exact, 4, MidpointRounding.AwayFromZero),
                DescribeParts(party, unitLabel)));
            builder.Exact += exact;

            if (lease.ExcludedCostTypes.Contains(type))
            {
                builder.NotAgreed += share;
                builder.Protocol.Add(new ProtocolEntry(
                    "Kostenart nicht vereinbart",
                    "Anteil bleibt beim Vermieter",
                    $"{Money(share)} nicht umlagefähig laut Mietvertrag",
                    0m));
                landlordHere.Add(new LandlordPosition(type, LandlordReason.NotAgreed, lease.Label, share));
            }
            else if (!OperatingStatementFor(lease))
            {
                var reason = IsFlatRateRoom(lease) ? LandlordReason.SharedFlatFlatRate : LandlordReason.FlatRate;
                builder.Protocol.Add(new ProtocolEntry(
                    "Pauschale / keine Abrechnung",
                    "Anteil wird nicht abgerechnet, sondern mit den Pauschalzahlungen verglichen",
                    Money(share),
                    0m));
                landlordHere.Add(new LandlordPosition(type, reason, lease.Label, share));
                actualByLease[lease.Id] = actualByLease.GetValueOrDefault(lease.Id) + share;
            }
            else
            {
                builder.Share += share;
                builder.Protocol.Add(new ProtocolEntry(
                    "Rundung auf Cent (Restcent-Verteilung)",
                    "Anteile werden auf Cent abgerundet; fehlende Cents erhalten die größten Nachkommastellen, damit die Summe aller Anteile genau den Kosten entspricht",
                    $"{Money(exact)} → {Money(share)}",
                    share,
                    share == CentDistribution.Cents(exact) ? null : $"Differenz {Money(share - CentDistribution.Cents(exact))} durch Restcent-Verteilung"));
            }
        }
    }

    private void MergeGroup(GroupResult result)
    {
        costTypes.AddRange(result.CostTypes);
        landlord.AddRange(result.Landlord);
        sections.AddRange(result.Sections ?? []);
        foreach (var (leaseId, amount) in result.FlatRateActuals ?? new Dictionary<Guid, decimal>())
        {
            actualByLease[leaseId] = actualByLease.GetValueOrDefault(leaseId) + amount;
        }

        foreach (var problem in result.Problems)
        {
            Problem(problem.Severity, problem.Text, problem.CostType);
        }

        foreach (var line in result.TenantLines)
        {
            if (!linesByLease.TryGetValue(line.LeaseId, out var lines))
            {
                lines = [];
                linesByLease[line.LeaseId] = lines;
            }

            lines.Add(line.Line);
        }
    }

    // ---- tenant statement ------------------------------------------------------------------------------------------

    private (decimal Paid, decimal Due, List<string> Months) Prepayments(BillingLease lease, params RentComponentType[] components)
    {
        decimal paid = 0, due = 0;
        var months = new List<string>();
        foreach (var month in lease.Prepayments.Where(p => components.Contains(p.Component)).OrderBy(p => p.ServiceMonth).ThenBy(p => p.Component))
        {
            var monthPeriod = Period.Month(month.ServiceMonth.Year, month.ServiceMonth.Month);
            var days = monthPeriod.DaysIn(billing);
            if (days == 0)
            {
                continue;
            }

            var factor = (decimal)days / monthPeriod.Days;
            var monthPaid = CentDistribution.Cents(month.Paid * factor);
            var monthDue = CentDistribution.Cents(month.Due * factor);
            paid += monthPaid;
            due += monthDue;
            months.Add($"{month.ServiceMonth:MM/yyyy}: gezahlt {Money(monthPaid)} (Soll {Money(monthDue)}){(days == monthPeriod.Days ? string.Empty : $" × {days}/{monthPeriod.Days} Tage")}");
        }

        return (paid, due, months);
    }

    /// <summary>"Unit" or "Unit, Zimmer 2 und Zimmer 3" for a lease on rooms.</summary>
    private static string RentalText(BillingUnit unit, BillingLease lease)
    {
        var names = unit.Rooms.Where(r => lease.CoversRoom(r.Id)).Select(r => r.Name).ToList();
        return names.Count == 0 ? unit.Name : $"{unit.Name}, {string.Join(" und ", names)}";
    }

    private TenantStatement Statement(BillingLease lease)
    {
        var unit = unitsById[lease.UnitId];
        var rental = RentalText(unit, lease);
        var operatingBilled = OperatingStatementFor(lease);
        var lines = (linesByLease.GetValueOrDefault(lease.Id) ?? [])
            .Where(l => operatingBilled || l.Group != CostGroup.Operating)
            .OrderBy(l => l.Group)
            .ThenBy(l => CostTypes.All.ToList().FindIndex(t => t.Key == l.CostType) is var i and >= 0 ? i : int.MaxValue)
            .ToList();
        var days = lease.Term.DaysIn(billing);
        var protocol = new List<ProtocolEntry>();
        var intersection = lease.Term.Intersect(billing)!.Value;
        protocol.Add(new ProtocolEntry(
            "Mietzeit im Abrechnungszeitraum",
            "Tage = Ende − Beginn + 1 (Überschneidung von Mietvertrag und Abrechnungszeitraum)",
            Span(intersection),
            days));

        var groups = new List<GroupBalance>();
        if (operatingBilled)
        {
            var share = lines.Where(l => l.Group == CostGroup.Operating).Sum(l => l.Share);
            var (paid, due, months) = Prepayments(lease, RentComponentType.OperatingCostPrepayment);
            protocol.Add(new ProtocolEntry(
                "Vorauszahlungen Betriebskosten",
                "Σ tatsächlich gezahlte Beträge je Leistungsmonat × Anteil des Monats im Abrechnungszeitraum",
                months.Count == 0 ? "keine Vorauszahlungen im Zeitraum" : string.Join("; ", months),
                paid));
            if (paid < due)
            {
                Problem(ProblemSeverity.Warning, $"{lease.Label}: Von den Vorauszahlungen für Betriebskosten sind {Money(due - paid)} noch offen (Soll {Money(due)}, gezahlt {Money(paid)}). Berücksichtigt wird nur, was gezahlt wurde.");
            }

            protocol.Add(new ProtocolEntry(
                "Ergebnis Betriebskosten",
                "Summe der Anteile − gezahlte Vorauszahlungen (positiv = Nachzahlung, negativ = Guthaben)",
                $"{Money(share)} − {Money(paid)}",
                share - paid));
            groups.Add(new GroupBalance(CostGroup.Operating, true, share, paid, due, share - paid));
        }

        var heatingLines = lines.Where(l => l.Group == CostGroup.Heating).ToList();
        var heatingCalculated = calculators.Any(c => c.Group == CostGroup.Heating);
        var (heatingPaid, heatingDue, heatingMonths) = Prepayments(lease, RentComponentType.HeatingCostPrepayment);
        if (heatingPaid != 0 || heatingDue != 0 || heatingCalculated)
        {
            var heatingShare = heatingLines.Sum(l => l.Share);
            groups.Add(new GroupBalance(CostGroup.Heating, heatingCalculated, heatingShare, heatingPaid, heatingDue, heatingShare - heatingPaid));
            protocol.Add(new ProtocolEntry(
                "Vorauszahlungen Heizkosten",
                "Σ tatsächlich gezahlte Beträge je Leistungsmonat × Anteil des Monats im Abrechnungszeitraum",
                heatingMonths.Count == 0 ? "keine Vorauszahlungen im Zeitraum" : string.Join("; ", heatingMonths),
                heatingPaid,
                heatingCalculated ? null : "Werden mit der Heizkostenabrechnung verrechnet, nicht mit den Betriebskosten."));
        }

        var household = lines.Sum(l => l.HouseholdLabor);
        var tradesman = lines.Sum(l => l.TradesmanLabor);
        if (household != 0 || tradesman != 0)
        {
            protocol.Add(new ProtocolEntry(
                "Arbeitskosten nach § 35a EStG",
                "Σ Arbeitskosten der Kostenart × Anteil des Mieters an den Kosten",
                $"haushaltsnahe Dienstleistungen {Money(household)}, Handwerkerleistungen {Money(tradesman)}",
                household + tradesman));
        }

        return new TenantStatement(lease.Id, lease.Label, rental, lease.Term, days, lines, groups, household, tradesman, protocol);
    }

    private FlatRateComparison FlatRate(BillingLease lease)
    {
        var unit = unitsById[lease.UnitId];
        var (paid, due, _) = Prepayments(lease, RentComponentType.OperatingCostFlatRate, RentComponentType.OperatingCostPrepayment);
        return new FlatRateComparison(lease.Id, lease.Label, RentalText(unit, lease), actualByLease.GetValueOrDefault(lease.Id), paid, due);
    }
}
