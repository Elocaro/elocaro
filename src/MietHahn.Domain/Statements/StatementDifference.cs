using MietHahn.Domain.Common;
using MietHahn.Domain.OperatingCosts;

namespace MietHahn.Domain.Statements;

/// <param name="Previous">Balance of the replaced revision; <c>null</c> if the tenant was not part of it.</param>
/// <param name="Current">Balance of the new revision; <c>null</c> if the tenant is no longer part of it.</param>
public sealed record LeaseDifference(Guid LeaseId, string Label, decimal? Previous, decimal? Current)
{
    /// <summary>Amount to pay additionally (positive) or to credit (negative) because of the correction.</summary>
    public decimal Difference => (Current ?? 0m) - (Previous ?? 0m);
}

/// <summary>Difference statement of a corrected operating cost statement against the replaced revision.</summary>
public sealed record StatementDifference(IReadOnlyList<LeaseDifference> Leases)
{
    public static StatementDifference Between(BillingResult previous, BillingResult current)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);
        var before = previous.Tenants.ToDictionary(t => t.LeaseId);
        var now = current.Tenants.ToDictionary(t => t.LeaseId);
        var rows = before.Keys.Union(now.Keys)
            .Select(id => new LeaseDifference(
                id,
                (now.GetValueOrDefault(id) ?? before[id]).Label,
                before.TryGetValue(id, out var b) ? b.Balance : null,
                now.TryGetValue(id, out var c) ? c.Balance : null))
            .Where(r => r.Difference != 0m)
            .OrderBy(r => r.Label, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return new StatementDifference(rows);
    }

    /// <summary>Sum of the claims against tenants that arise from the correction.</summary>
    public decimal AdditionalClaims => Leases.Where(l => l.Difference > 0).Sum(l => l.Difference);

    public decimal Credits => -Leases.Where(l => l.Difference < 0).Sum(l => l.Difference);

    /// <summary>
    /// After the deadline additional payments can no longer be claimed (§ 556 para. 3 sentence 3 BGB) unless the landlord
    /// is not responsible for the delay. Credits remain due.
    /// </summary>
    public bool ClaimsExcluded(Period billingPeriod, DateOnly today) => AdditionalClaims > 0 && today > StatementDeadline.Of(billingPeriod);
}
