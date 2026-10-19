using MietHahn.Domain.Common;
using MietHahn.Domain.Metering;
using MietHahn.Domain.OperatingCosts;

namespace MietHahn.Domain.Heating;

/// <summary>Weights of the parties (tenants, vacancies) for the four distribution bases.</summary>
internal sealed partial class HeatingRun
{
    private enum PartyKind
    {
        Lease,
        Vacancy,
        Unassigned,
    }

    private enum Basis
    {
        HeatConsumption,
        HeatArea,
        WaterConsumption,
        WaterArea,
    }

    /// <param name="PerDay">The value is a value per day times <paramref name="Days"/> (area); otherwise a measured quantity.</param>
    private sealed record WeightPart(Period Span, string Text, bool PerDay, int Days, decimal Value);

    private sealed class Party(string key, PartyKind kind, BillingLease? lease, string label, string sortKey)
    {
        private readonly Dictionary<Basis, List<WeightPart>> parts = [];

        public string Key { get; } = key;

        public PartyKind Kind { get; } = kind;

        public BillingLease? Lease { get; } = lease;

        public string Label { get; } = label;

        public string SortKey { get; } = sortKey;

        public decimal Weight(Basis basis) => parts.TryGetValue(basis, out var list) ? list.Sum(p => p.Value) : 0m;

        public List<WeightPart> Parts(Basis basis) => parts.TryGetValue(basis, out var list) ? list : [];

        public void Add(Basis basis, WeightPart part)
        {
            if (!parts.TryGetValue(basis, out var list))
            {
                list = [];
                parts[basis] = list;
            }

            if (list.Count > 0 && list[^1] is { } last && last.Text == part.Text && last.PerDay == part.PerDay && last.Span.To!.Value.AddDays(1) == part.Span.From)
            {
                list[^1] = last with { Span = new Period(last.Span.From, part.Span.To), Days = last.Days + part.Days, Value = last.Value + part.Value };
                return;
            }

            list.Add(part);
        }
    }

    private static string Describe(Party party, Basis basis, string unit) =>
        string.Join("; ", party.Parts(basis).Select(p => p.PerDay
            ? $"{Span(p.Span)}: {p.Text} × {p.Days} Tage = {Num(p.Value)} {unit}"
            : $"{Span(p.Span)}: {Num(p.Value)} {unit} ({p.Text})"));

    private BillingLease? LeaseAt(Guid unitId, Guid? roomId, DateOnly day) =>
        leases.FirstOrDefault(l => l.UnitId == unitId && (roomId is null || l.RoomId == roomId) && l.Term.Contains(day));

    private static Party OccupantParty(Dictionary<string, Party> registry, BillingUnit unit, BillingRoom? room, BillingLease? lease)
    {
        string key;
        Party Create(string k, PartyKind kind, BillingLease? l, string label, string sortKey)
        {
            if (!registry.TryGetValue(k, out var party))
            {
                party = new Party(k, kind, l, label, sortKey);
                registry[k] = party;
            }

            return party;
        }

        if (lease is not null)
        {
            key = $"L:{lease.Id}";
            return Create(key, PartyKind.Lease, lease, lease.Label, $"0|{lease.Term.From:yyyyMMdd}|{lease.Id}");
        }

        var subject = room?.Id ?? unit.Id;
        var vacancy = room is null ? $"Leerstand {unit.Name}" : $"Leerstand {unit.Name}, {room.Name}";
        return Create($"V:{subject}", PartyKind.Vacancy, null, vacancy, $"1|{vacancy}|{subject}");
    }

    private List<Period> Segments(IEnumerable<Period> boundaries)
    {
        var points = new SortedSet<DateOnly> { billing.From };
        foreach (var boundary in boundaries)
        {
            if (boundary.From > billing.From && boundary.From <= billingEnd)
            {
                points.Add(boundary.From);
            }

            if (boundary.To is { } to && to >= billing.From && to < billingEnd)
            {
                points.Add(to.AddDays(1));
            }
        }

        var list = points.ToList();
        return [.. list.Select((start, i) => new Period(start, i + 1 < list.Count ? list[i + 1].AddDays(-1) : billingEnd))];
    }

    private List<Party> BuildParties(bool heat, bool water)
    {
        var registry = new Dictionary<string, Party>();
        foreach (var unit in units)
        {
            if (unit.Rooms.Count == 0)
            {
                AddPlainUnit(unit, registry, heat, water);
            }
            else
            {
                AddSharedFlat(unit, registry, heat, water);
            }
        }

        return [.. registry.Values.OrderBy(p => p.SortKey, StringComparer.Ordinal)];
    }

    private AreaState? AreaOf(BillingUnit unit, DateOnly day)
    {
        var state = unit.Areas.FirstOrDefault(a => a.Validity.Contains(day));
        if (state is null)
        {
            Problem(ProblemSeverity.Error, $"Für {unit.Name} ist am {Day(day)} keine Fläche hinterlegt.", HeatingCostCalculator.HeatKey);
        }

        return state;
    }

    private void AddPlainUnit(BillingUnit unit, Dictionary<string, Party> registry, bool heat, bool water)
    {
        var segments = Segments(leases.Where(l => l.UnitId == unit.Id).Select(l => l.Term).Concat(unit.Areas.Select(a => a.Validity)));
        var blocks = new List<(Period Span, Party Party)>();
        foreach (var segment in segments)
        {
            var party = OccupantParty(registry, unit, null, LeaseAt(unit.Id, null, segment.From));
            if (AreaOf(unit, segment.From) is { } state)
            {
                if (heat)
                {
                    party.Add(Basis.HeatArea, new WeightPart(segment, $"{Num(state.HeatedArea)} m²", true, segment.Days, state.HeatedArea * segment.Days));
                }

                if (water)
                {
                    party.Add(Basis.WaterArea, new WeightPart(segment, $"{Num(state.LivingArea)} m²", true, segment.Days, state.LivingArea * segment.Days));
                }
            }

            if (blocks.Count > 0 && ReferenceEquals(blocks[^1].Party, party))
            {
                blocks[^1] = (new Period(blocks[^1].Span.From, segment.To), party);
            }
            else
            {
                blocks.Add((segment, party));
            }
        }

        if (heat)
        {
            AddConsumption(unit, blocks, MeterType.Heat, Basis.HeatConsumption, true);
        }

        if (water)
        {
            AddConsumption(unit, blocks, MeterType.HotWater, Basis.WaterConsumption, false);
        }
    }

    private void AddConsumption(BillingUnit unit, List<(Period Span, Party Party)> blocks, MeterType type, Basis basis, bool byDegreeDays)
    {
        var typeName = type == MeterType.Heat ? "Wärme" : "Warmwasser";
        var quantities = blocks.Select(b => Consumption(unit.Id, type, b.Span)).ToList();
        if (quantities.All(q => q.Value is not null) && !InnerBoundaryInterpolated(blocks, quantities))
        {
            for (var i = 0; i < blocks.Count; i++)
            {
                var text = quantities[i].Interpolated ? "interpolierter Zählerstand" : blocks.Count > 1 ? "Zwischenablesung" : "Ablesung";
                blocks[i].Party.Add(basis, new WeightPart(blocks[i].Span, text, false, blocks[i].Span.Days, quantities[i].Value!.Value));
            }

            return;
        }

        var whole = Consumption(unit.Id, type, billing);
        if (whole.Value is not { } total)
        {
            Problem(ProblemSeverity.Error, $"{unit.Name}: {whole.Note ?? $"{typeName}-Verbrauch nicht ermittelbar."}", HeatingCostCalculator.HeatKey);
            return;
        }

        // Tenant change without an interim reading: heating by degree days (§ 9b para. 2 HeizkostenV), hot water by days.
        var table = DegreeDayTables.For(billingEnd.Year);
        var weights = blocks.Select(b => byDegreeDays ? table.Weight(b.Span.From, b.Span.To!.Value) : b.Span.Days).ToList();
        var weightSum = weights.Sum();
        if (byDegreeDays)
        {
            Problem(
                ProblemSeverity.Warning,
                $"Mieterwechsel in {unit.Name} ohne Zwischenablesung: Der Heizverbrauch wird nach Gradtagszahlen auf die Nutzer verteilt (Tabelle {table.Version}: {table.Status}).",
                HeatingCostCalculator.HeatKey);
        }
        else
        {
            Problem(ProblemSeverity.Warning, $"Mieterwechsel in {unit.Name} ohne Zwischenablesung: Der Warmwasserverbrauch wird nach Tagen auf die Nutzer verteilt.", HeatingCostCalculator.HotWaterKey);
        }

        for (var i = 0; i < blocks.Count; i++)
        {
            var share = weightSum == 0 ? 0m : weights[i] / weightSum;
            var text = byDegreeDays
                ? $"Gradtagszahlen {Pct(share)} von {Num(total)} kWh (Tabelle {table.Version})"
                : $"nach Tagen {Pct(share)} von {Num(total)} m³";
            blocks[i].Party.Add(basis, new WeightPart(blocks[i].Span, text, false, blocks[i].Span.Days, total * share));
        }
    }

    private static bool InnerBoundaryInterpolated(List<(Period Span, Party Party)> blocks, List<ConsumptionQuantity> quantities)
    {
        for (var i = 0; i + 1 < blocks.Count; i++)
        {
            var boundary = blocks[i].Span.To!.Value;
            foreach (var quantity in new[] { quantities[i], quantities[i + 1] })
            {
                if (quantity.InterpolatedDays?.Contains(boundary) ?? quantity.Interpolated)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void AddSharedFlat(BillingUnit unit, Dictionary<string, Party> registry, bool heat, bool water)
    {
        var rooms = unit.Rooms.OrderBy(r => r.Name, StringComparer.Ordinal).ThenBy(r => r.Id).ToList();
        var boundaries = leases.Where(l => l.UnitId == unit.Id).Select(l => l.Term)
            .Concat(unit.Areas.Select(a => a.Validity))
            .Concat(rooms.SelectMany(r => r.Areas.Select(a => a.Validity)));
        var dayShares = new Dictionary<Party, decimal>();
        foreach (var segment in Segments(boundaries))
        {
            var state = AreaOf(unit, segment.From);
            if (state is null)
            {
                continue;
            }

            var areas = rooms.Select(r => r.Areas.FirstOrDefault(a => a.Validity.Contains(segment.From))?.AllocationArea ?? 0m).ToList();
            var areaTotal = areas.Sum();
            if (areaTotal <= 0)
            {
                Problem(ProblemSeverity.Error, $"Für die Zimmer der Wohngemeinschaft {unit.Name} ist am {Day(segment.From)} keine Fläche hinterlegt; der Anteil der Wohnung kann nicht auf die Zimmer verteilt werden.", HeatingCostCalculator.HeatKey);
                if (!registry.TryGetValue("U:" + unit.Id, out var unassigned))
                {
                    unassigned = new Party("U:" + unit.Id, PartyKind.Unassigned, null, $"nicht verteilbar ({unit.Name})", "3|U|" + unit.Id);
                    registry[unassigned.Key] = unassigned;
                }

                if (heat)
                {
                    unassigned.Add(Basis.HeatArea, new WeightPart(segment, $"{Num(state.HeatedArea)} m²", true, segment.Days, state.HeatedArea * segment.Days));
                }

                if (water)
                {
                    unassigned.Add(Basis.WaterArea, new WeightPart(segment, $"{Num(state.LivingArea)} m²", true, segment.Days, state.LivingArea * segment.Days));
                }

                dayShares[unassigned] = dayShares.GetValueOrDefault(unassigned) + segment.Days;
                continue;
            }

            for (var i = 0; i < rooms.Count; i++)
            {
                var party = OccupantParty(registry, unit, rooms[i], LeaseAt(unit.Id, rooms[i].Id, segment.From));
                var share = areas[i] / areaTotal;
                var text = $"{Num(state.HeatedArea)} m², Zimmeranteil {Num(areas[i])} von {Num(areaTotal)} m²";
                if (heat)
                {
                    party.Add(Basis.HeatArea, new WeightPart(segment, text, true, segment.Days, state.HeatedArea * segment.Days * share));
                }

                if (water)
                {
                    party.Add(Basis.WaterArea, new WeightPart(segment, $"{Num(state.LivingArea)} m², Zimmeranteil {Num(areas[i])} von {Num(areaTotal)} m²", true, segment.Days, state.LivingArea * segment.Days * share));
                }

                dayShares[party] = dayShares.GetValueOrDefault(party) + (segment.Days * share);
            }
        }

        foreach (var (type, basis) in new[] { (MeterType.Heat, Basis.HeatConsumption), (MeterType.HotWater, Basis.WaterConsumption) })
        {
            if ((basis == Basis.HeatConsumption && !heat) || (basis == Basis.WaterConsumption && !water))
            {
                continue;
            }

            var whole = Consumption(unit.Id, type, billing);
            if (whole.Value is not { } total)
            {
                Problem(ProblemSeverity.Error, $"{unit.Name}: {whole.Note ?? "Verbrauch nicht ermittelbar."}", HeatingCostCalculator.HeatKey);
                continue;
            }

            var daySum = dayShares.Values.Sum();
            foreach (var (party, days) in dayShares)
            {
                var share = daySum == 0 ? 0m : days / daySum;
                party.Add(basis, new WeightPart(billing, $"Zimmeranteil nach Fläche und Tagen {Pct(share)} von {Num(total)}", false, billingDays, total * share));
            }
        }
    }
}
