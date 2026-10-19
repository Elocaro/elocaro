using MietHahn.Domain.Common;
using MietHahn.Domain.Leases;
using MietHahn.Domain.Metering;
using MietHahn.Domain.OperatingCosts;
using MietHahn.Domain.Receipts;

namespace MietHahn.Domain.Tests.OperatingCosts;

/// <summary>Helpers to build the input of the calculation core in tests.</summary>
internal static class Scenario
{
    public static readonly Period Year2026 = Period.CalendarYear(2026);

    public static Guid Id(int n) => new(n, 0, 0, new byte[8]);

    public static DateOnly D(int year, int month, int day) => new(year, month, day);

    public static Period Span(DateOnly from, DateOnly? to = null) => new(from, to);

    public static BillingUnit Unit(int id, string name, decimal living, decimal? heated = null, SharedFlatBilling flat = SharedFlatBilling.FlatRate, params BillingRoom[] rooms) =>
        new(Id(id), name, [new AreaState(new Period(D(2000, 1, 1)), living, heated ?? living)], rooms, flat);

    public static BillingRoom Room(int id, string name, decimal area) =>
        new(Id(id), name, [new RoomAreaState(new Period(D(2000, 1, 1)), area)]);

    public static BillingLease Lease(
        int id,
        string label,
        int unit,
        DateOnly from,
        DateOnly? to = null,
        int persons = 1,
        int? room = null,
        bool statement = true,
        IReadOnlyList<PrepaymentMonth>? prepayments = null,
        params string[] excluded) =>
        new(
            Id(id),
            label,
            Id(unit),
            room is { } r ? Id(r) : null,
            new Period(from, to),
            [new PersonsState(new Period(from, to), persons)],
            statement,
            excluded.ToHashSet(),
            prepayments ?? []);

    public static BillingCost Cost(int id, string type, decimal amount, Period? period = null, bool allocable = true, int? unit = null, string description = "Rechnung") =>
        new(Id(id), type, description, amount, period ?? Year2026, allocable, unit is { } u ? Id(u) : null);

    public static BillingRule Rule(string type, AllocationKey key, DateOnly? from = null, DateOnly? to = null, MeterType? meter = null, decimal? percent = null, decimal? amount = null, params (int Unit, decimal Percent)[] shares) =>
        new(type, new Period(from ?? D(2000, 1, 1), to), key, meter, percent, amount, null, [.. shares.Select(a => new FixedShare(Id(a.Unit), a.Percent))]);

    public static BillingInput Input(
        IReadOnlyList<BillingUnit> units,
        IReadOnlyList<BillingLease> leases,
        IReadOnlyList<BillingCost> costs,
        IReadOnlyList<BillingRule> rules,
        IConsumptionSource? consumption = null,
        Period? period = null) =>
        new(period ?? Year2026, units, leases, costs, rules, consumption ?? new RateConsumption());

    public static PrepaymentMonth Prepayment(int year, int month, decimal due, decimal paid, RentComponentType component = RentComponentType.OperatingCostPrepayment) =>
        new(new DateOnly(year, month, 1), component, due, paid);

    /// <summary>Prepayments for consecutive months of a year.</summary>
    public static List<PrepaymentMonth> Months(int year, int from, int to, decimal amount, RentComponentType component = RentComponentType.OperatingCostPrepayment) =>
        [.. Enumerable.Range(from, to - from + 1).Select(m => Prepayment(year, m, amount, amount, component))];

    public static TenantStatement Statement(BillingResult result, int lease) => result.Tenants.Single(t => t.LeaseId == Id(lease));

    public static TenantCostLine Line(BillingResult result, int lease, string type) => Statement(result, lease).Lines.Single(l => l.CostType == type);
}

/// <summary>Consumption source with a constant consumption per day and unit (linear, like interpolated readings).</summary>
internal sealed class RateConsumption(params (int Unit, decimal PerDay)[] rates) : IConsumptionSource
{
    private readonly Dictionary<Guid, decimal> perDay = rates.ToDictionary(r => Scenario.Id(r.Unit), r => r.PerDay);

    public List<(Guid Unit, MeterType Type, Period Period)> Calls { get; } = [];

    public ConsumptionQuantity Quantity(Guid unitId, MeterType type, Period period)
    {
        Calls.Add((unitId, type, period));
        return perDay.TryGetValue(unitId, out var rate)
            ? new ConsumptionQuantity(rate * period.Days)
            : new ConsumptionQuantity(null, false, "Kein Zähler zugeordnet.");
    }
}
