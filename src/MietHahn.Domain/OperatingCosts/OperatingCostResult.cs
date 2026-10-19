using MietHahn.Domain.Common;
using MietHahn.Domain.Receipts;

namespace MietHahn.Domain.OperatingCosts;

/// <summary>One step of the calculation protocol: what was calculated (title), by which formula, with which values, with which result.</summary>
public sealed record ProtocolEntry(string Title, string Formula, string Values, decimal? Result, string? Note = null);

public sealed record CalculationProblem(ProblemSeverity Severity, string Text, string? CostType = null);

/// <summary>An invoice item as it enters the billing period.</summary>
/// <param name="Prorated">Share of the amount that falls into the billing period (exact, not rounded).</param>
public sealed record CostItemLine(Guid Id, string Description, decimal Amount, Period Period, decimal Prorated, string Calculation);

public sealed record LandlordPosition(string CostType, LandlordReason Reason, string Label, decimal Amount);

/// <param name="Total">Costs of the type in the billing period, rounded to cents (sum of all shares).</param>
/// <param name="Key">Description of the allocation key(s) used, e.g. "Wohnfläche" or "Wohnfläche, ab 01.07.2026 Personen".</param>
public sealed record CostTypeResult(
    string CostType,
    string Name,
    CostGroup Group,
    decimal Total,
    string Key,
    IReadOnlyList<CostItemLine> Items,
    decimal TenantTotal,
    decimal LandlordTotal,
    IReadOnlyList<LandlordPosition> Landlord);

/// <summary>Share of a tenant in a cost type.</summary>
/// <param name="Share">Share in cents, charged to the tenant.</param>
/// <param name="Exact">Exact share before rounding (for the protocol).</param>
/// <param name="NotAgreed">Share that falls on the landlord because the cost type is not agreed in the lease.</param>
/// <param name="HouseholdLabor">Share of labor costs under § 35a para. 2 EStG (household-related services).</param>
/// <param name="TradesmanLabor">Share of labor costs under § 35a para. 3 EStG (tradesman services).</param>
public sealed record TenantCostLine(
    string CostType,
    string Name,
    CostGroup Group,
    decimal CostTotal,
    string Key,
    string Basis,
    decimal Share,
    decimal Exact,
    decimal NotAgreed,
    decimal HouseholdLabor,
    decimal TradesmanLabor,
    IReadOnlyList<ProtocolEntry> Protocol);

/// <param name="Calculated"><c>false</c> if no core exists for the group yet (heating costs).</param>
/// <param name="Balance">Share minus prepayments paid; positive = additional payment, negative = credit.</param>
public sealed record GroupBalance(CostGroup Group, bool Calculated, decimal Share, decimal PrepaymentsPaid, decimal PrepaymentsDue, decimal Balance);

public sealed record TenantStatement(
    Guid LeaseId,
    string Label,
    string Rental,
    Period Term,
    int Days,
    IReadOnlyList<TenantCostLine> Lines,
    IReadOnlyList<GroupBalance> Groups,
    decimal HouseholdLabor,
    decimal TradesmanLabor,
    IReadOnlyList<ProtocolEntry> Protocol)
{
    /// <summary>Sum of the operating cost shares.</summary>
    public decimal Share => Lines.Where(l => l.Group == CostGroup.Operating).Sum(l => l.Share);

    /// <summary>Balance of all calculated groups; positive = additional payment.</summary>
    public decimal Balance => Groups.Where(g => g.Calculated).Sum(g => g.Balance);

    public GroupBalance? Operating => Groups.FirstOrDefault(g => g.Group == CostGroup.Operating);
}

/// <summary>Internal comparison for a lease that gets no statement (flat rate): actual costs against flat-rate income.</summary>
public sealed record FlatRateComparison(Guid LeaseId, string Label, string Rental, decimal ActualCosts, decimal Paid, decimal Due)
{
    /// <summary>Actual costs minus income; positive = the flat rate does not cover the costs.</summary>
    public decimal Difference => ActualCosts - Paid;
}

/// <summary>Costs of a group for which no core exists yet.</summary>
public sealed record UncalculatedGroup(CostGroup Group, string Name, decimal Total, IReadOnlyList<CostItemLine> Items);

/// <summary>Result of the operating cost calculation including the calculation protocol.</summary>
public sealed record BillingResult(
    Period BillingPeriod,
    IReadOnlyList<CostTypeResult> CostTypes,
    IReadOnlyList<TenantStatement> Tenants,
    IReadOnlyList<FlatRateComparison> FlatRates,
    IReadOnlyList<LandlordPosition> Landlord,
    IReadOnlyList<CostItemLine> NonAllocable,
    IReadOnlyList<UncalculatedGroup> UncalculatedGroups,
    IReadOnlyList<CalculationProblem> Problems)
{
    public bool HasErrors => Problems.Any(p => p.Severity == ProblemSeverity.Error);

    /// <summary>Total costs of the calculated cost types.</summary>
    public decimal TotalCosts => CostTypes.Sum(t => t.Total);

    public decimal TenantTotal => CostTypes.Sum(t => t.TenantTotal);

    public decimal LandlordTotal => CostTypes.Sum(t => t.LandlordTotal);
}
