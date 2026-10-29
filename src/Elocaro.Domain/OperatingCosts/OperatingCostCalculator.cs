using Elocaro.Domain.Common;

namespace Elocaro.Domain.OperatingCosts;

/// <summary>
/// Calculation core for the operating cost statement (BetrKV, § 556 BGB): pure function without database access.
/// Costs are assigned to the billing period by their service period, distributed per cost type by the allocation key
/// of the property (day-exact per user, so a tenant change is split correctly), vacancy stays with the landlord.
/// All shares are rounded to cents with the largest remainder method, so tenant shares and landlord shares add up to the
/// total costs of every cost type exactly. Every step is recorded in the calculation protocol.
/// Further cost groups (heating costs) are plugged in via <see cref="ICostGroupCalculator"/>.
/// </summary>
public static class OperatingCostCalculator
{
    public static BillingResult Calculate(BillingInput input, IEnumerable<ICostGroupCalculator>? groupCalculators = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        return new CalculationRun(input, groupCalculators?.ToList() ?? []).Execute();
    }
}
