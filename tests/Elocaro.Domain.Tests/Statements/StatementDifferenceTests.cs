using Elocaro.Domain.Common;
using Elocaro.Domain.OperatingCosts;
using Elocaro.Domain.Statements;
using static Elocaro.Domain.Tests.OperatingCosts.Scenario;

namespace Elocaro.Domain.Tests.Statements;

public class StatementDifferenceTests
{
    private static BillingResult Calculate(decimal cost, bool bensLease = true) =>
        OperatingCostCalculator.Calculate(Input(
            [Unit(1, "Wohnung 1", 60m), Unit(2, "Wohnung 2", 40m)],
            [
                Lease(1, "Anna", 1, D(2024, 1, 1), prepayments: Months(2026, 1, 12, 20m)),
                .. bensLease ? new[] { Lease(2, "Ben", 2, D(2024, 1, 1), prepayments: Months(2026, 1, 12, 20m)) } : [],
            ],
            [Cost(1, "betrkv-01", cost)],
            [Rule("betrkv-01", AllocationKey.LivingArea)]));

    [Fact]
    public void Difference_is_the_change_of_each_balance()
    {
        // Costs 1,000 → 1,100: Anna 600 → 660 (balance 360 → 420), Ben 400 → 440 (160 → 200)
        var difference = StatementDifference.Between(Calculate(1000m), Calculate(1100m));

        Assert.Equal([60m, 40m], difference.Leases.OrderBy(l => l.Label).Select(l => l.Difference));
        Assert.Equal(100m, difference.AdditionalClaims);
        Assert.Equal(0m, difference.Credits);
    }

    [Fact]
    public void Credits_and_unchanged_tenants_are_separated()
    {
        var difference = StatementDifference.Between(Calculate(1000m), Calculate(900m, bensLease: false));

        // Anna: 600 → 540 (-60 credit); Ben is no longer part (balance 160 → none: -160)
        Assert.Equal(220m, difference.Credits);
        Assert.Equal(0m, difference.AdditionalClaims);
        Assert.Equal(-160m, difference.Leases.Single(l => l.Label == "Ben").Difference);
    }

    [Fact]
    public void Identical_results_have_no_difference()
    {
        Assert.Empty(StatementDifference.Between(Calculate(1000m), Calculate(1000m)).Leases);
    }

    [Fact]
    public void Additional_claims_after_the_deadline_are_excluded_but_credits_are_not()
    {
        var up = StatementDifference.Between(Calculate(1000m), Calculate(1100m));
        var down = StatementDifference.Between(Calculate(1000m), Calculate(900m));

        Assert.False(up.ClaimsExcluded(Year2026, new DateOnly(2027, 12, 31)));
        Assert.True(up.ClaimsExcluded(Year2026, new DateOnly(2028, 1, 1)));
        Assert.False(down.ClaimsExcluded(Year2026, new DateOnly(2028, 1, 1)));
    }
}
