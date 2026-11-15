using Elocaro.Domain.Receipts;

namespace Elocaro.Domain.Properties;

/// <summary>Rule for the cost types a tenant pays directly to the supplier (not through the landlord) in a <see cref="Unit"/>.</summary>
public static class SelfPaidCostTypes
{
    /// <summary>Keeps each key once, and only those of allocable cost types outside the heating group (heating is settled by consumption).</summary>
    public static IReadOnlyList<string> Normalize(IEnumerable<string> keys) =>
        [.. keys.Distinct(StringComparer.Ordinal).Where(k => CostTypes.Find(k) is { Allocable: true } && CostGroups.Of(k) != CostGroup.Heating)];
}
