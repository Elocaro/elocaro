using MietHahn.Domain.Common;
using MietHahn.Domain.Leases;
using MietHahn.Domain.Metering;
using MietHahn.Domain.OperatingCosts;
using MietHahn.Domain.Receipts;
using static MietHahn.Domain.Tests.OperatingCosts.Scenario;

namespace MietHahn.Domain.Tests.OperatingCosts;

public class CostAllocationRuleTests
{
    private static readonly Guid Property = Id(1);

    [Fact]
    public void A_new_key_ends_the_previous_one_the_day_before_and_the_same_day_replaces_it()
    {
        var series = new List<CostAllocationRule>();
        CostAllocationRule.SetFrom(series, Property, "betrkv-01", D(2025, 1, 1), AllocationKey.LivingArea);

        var second = CostAllocationRule.SetFrom(series, Property, "betrkv-01", D(2026, 1, 1), AllocationKey.Persons);
        var corrected = CostAllocationRule.SetFrom(series, Property, "betrkv-01", D(2026, 1, 1), AllocationKey.Units);

        Assert.Equal(2, series.Count);
        Assert.Equal(new Period(D(2025, 1, 1), D(2025, 12, 31)), series[0].Validity);
        Assert.Same(corrected, series[1]);
        Assert.NotSame(second, corrected);
        Assert.Throws<RuleViolationException>(() => CostAllocationRule.SetFrom(series, Property, "betrkv-01", D(2025, 6, 1), AllocationKey.Units));
    }

    [Fact]
    public void Ending_a_key_closes_the_current_validity()
    {
        var series = new List<CostAllocationRule>();
        CostAllocationRule.SetFrom(series, Property, "betrkv-01", D(2025, 1, 1), AllocationKey.LivingArea);

        CostAllocationRule.EndOn(series, D(2025, 12, 31));

        Assert.Equal(D(2025, 12, 31), series[0].Validity.To);
        Assert.Throws<RuleViolationException>(() => CostAllocationRule.EndOn(series, D(2027, 1, 1)));
    }

    [Theory]
    [InlineData("verwaltung", "nicht umlagefähig")]
    [InlineData("betrkv-05", "Heizkostenverordnung")]
    [InlineData("gibt-es-nicht", "Unbekannte Kostenart")]
    public void Only_allocable_operating_cost_types_have_a_key(string type, string message)
    {
        var error = Assert.Throws<RuleViolationException>(() => new CostAllocationRule(Property, type, new Period(D(2025, 1, 1)), AllocationKey.LivingArea));

        Assert.Contains(message, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Key_specific_data_is_validated()
    {
        var validity = new Period(D(2025, 1, 1));

        Assert.Throws<RuleViolationException>(() => new CostAllocationRule(Property, "betrkv-02", validity, AllocationKey.Consumption));
        Assert.Throws<RuleViolationException>(() => new CostAllocationRule(Property, "betrkv-14", validity, AllocationKey.FixedPercent));
        Assert.Throws<RuleViolationException>(() => new CostAllocationRule(Property, "betrkv-14", validity, AllocationKey.FixedPercent, shares: [new FixedShare(Id(2), 60m), new FixedShare(Id(3), 50m)]));
        Assert.Throws<RuleViolationException>(() => new CostAllocationRule(Property, "betrkv-14", validity, AllocationKey.FixedPercent, shares: [new FixedShare(Id(2), 30m), new FixedShare(Id(2), 20m)]));
        Assert.Throws<RuleViolationException>(() => new CostAllocationRule(Property, "betrkv-10", validity, AllocationKey.Units, preDeductionPercent: 10m, preDeductionAmount: 5m));
        Assert.Throws<RuleViolationException>(() => new CostAllocationRule(Property, "betrkv-10", validity, AllocationKey.Units, preDeductionPercent: 120m));

        var rule = new CostAllocationRule(Property, "betrkv-02", validity, AllocationKey.Consumption, MeterType.ColdWater, preDeductionPercent: 0m, shares: [new FixedShare(Id(2), 10m)]);
        Assert.Equal((MeterType.ColdWater, (decimal?)null), (rule.MeterType, rule.PreDeductionPercent));
        Assert.Empty(rule.Shares); // shares only count for the fixed-percent key
        Assert.Equal(AllocationKey.Consumption, rule.ToBillingRule().Key);
    }

    [Fact]
    public void Lease_excludes_cost_types_that_are_not_agreed()
    {
        var lease = new Lease(Id(5), Id(5), null, D(2025, 1, 1));

        lease.SetExcludedCostTypes(["betrkv-10", "betrkv-01", "betrkv-10"]);

        Assert.Equal(["betrkv-01", "betrkv-10"], lease.ExcludedCostTypes);
        Assert.False(lease.IsAgreed("betrkv-10"));
        Assert.True(lease.IsAgreed("betrkv-02"));
        Assert.Throws<RuleViolationException>(() => lease.SetExcludedCostTypes(["verwaltung"]));
        lease.SetExcludedCostTypes([]);
        Assert.True(lease.IsAgreed("betrkv-10"));
    }

    [Fact]
    public void Subtype_needs_a_known_cost_type_and_a_name()
    {
        var subtype = new CostSubtype("betrkv-13", "  Haftpflicht ");

        Assert.Equal("Haftpflicht", subtype.Name);
        Assert.Throws<RuleViolationException>(() => new CostSubtype("x", "Name"));
        Assert.Throws<RuleViolationException>(() => new CostSubtype("betrkv-13", " "));
        Assert.Throws<RuleViolationException>(() => subtype.Rename(new string('x', 101)));
    }
}

public class CentDistributionTests
{
    [Fact]
    public void Remaining_cents_go_to_the_largest_fractional_parts_and_equal_ones_to_the_first()
    {
        // 10.00 € in thirds: 3.33 each, the missing cent goes to the first
        Assert.Equal([3.34m, 3.33m, 3.33m], CentDistribution.Distribute(10m, [1m, 1m, 1m]));
        // 1.00 € to 0.6 : 0.4 : 0.0 → exact
        Assert.Equal([0.60m, 0.40m, 0m], CentDistribution.Distribute(1m, [6m, 4m, 0m]));
        Assert.Equal([-3.33m, -3.33m, -3.34m], CentDistribution.Distribute(-10m, [1m, 1m, 1m]));
        Assert.Equal([0m, 0m], CentDistribution.Distribute(5m, [0m, 0m]));
    }

    [Fact]
    public void Parts_always_add_up_to_the_total_for_random_data()
    {
        var random = new Random(28);
        for (var run = 0; run < 2000; run++)
        {
            var weights = Enumerable.Range(0, random.Next(1, 12)).Select(_ => random.Next(0, 5000) / 7m).ToList();
            if (weights.Sum() == 0)
            {
                continue;
            }

            var total = random.Next(-100000, 10000000) / 100m;
            var parts = CentDistribution.Distribute(total, weights);

            Assert.Equal(total, parts.Sum());
            Assert.All(parts, p => Assert.Equal(decimal.Round(p, 2), p));
            Assert.Equal(parts, CentDistribution.Distribute(total, weights));
        }
    }
}
