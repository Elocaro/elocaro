using Elocaro.Domain.Common;
using Elocaro.Domain.OperatingCosts;
using Elocaro.Domain.Statements;
using Elocaro.Domain.Tests.OperatingCosts;
using static Elocaro.Domain.Tests.OperatingCosts.Scenario;

namespace Elocaro.Domain.Tests.Statements;

public class PlausibilityCheckerTests
{
    private static BillingResult Calculate(Period period, IReadOnlyList<BillingCost> costs, IReadOnlyList<BillingLease>? leases = null, IReadOnlyList<BillingRule>? rules = null) =>
        OperatingCostCalculator.Calculate(Input(
            [Unit(1, "Wohnung 1", 60m), Unit(2, "Wohnung 2", 40m)],
            leases ?? [Lease(1, "Anna", 1, D(2024, 1, 1), prepayments: Months(period.From.Year, 1, 12, 50m)), Lease(2, "Ben", 2, D(2024, 1, 1), prepayments: Months(period.From.Year, 1, 12, 50m))],
            costs,
            rules ?? [Rule("betrkv-01", AllocationKey.LivingArea), Rule("betrkv-02", AllocationKey.LivingArea)],
            period: period));

    private static BillingCost Cost2025(int id, string type, decimal amount) => Cost(id, type, amount, Period.CalendarYear(2025));

    [Fact]
    public void Without_previous_year_there_is_no_comparison()
    {
        var current = Calculate(Year2026, [Cost(1, "betrkv-01", 1000m)]);

        var findings = PlausibilityChecker.Check(current, null);

        Assert.DoesNotContain(findings, f => f.Step == WizardStep.Costs);
    }

    [Fact]
    public void Missing_recurring_cost_type_and_large_deviation_are_flagged_in_the_cost_step()
    {
        var previous = Calculate(Period.CalendarYear(2025), [Cost2025(1, "betrkv-01", 600m), Cost2025(2, "betrkv-02", 200m), Cost2025(3, "betrkv-08", 100m)]);
        var current = Calculate(Year2026, [Cost(1, "betrkv-01", 1000m), Cost(3, "betrkv-08", 110m), Cost(4, "betrkv-13", 90m)]);

        var findings = PlausibilityChecker.Check(current, previous, 20m).Where(f => f.Step == WizardStep.Costs).ToList();

        // 600 → 1,000 is +66.7 %; betrkv-02 is missing; betrkv-08 +10 % is within the limit; betrkv-13 is new.
        Assert.Equal(3, findings.Count);
        Assert.Contains(findings, f => f is { CostType: "betrkv-01", Severity: FindingSeverity.Warning } && f.Text.Contains("+66,7 %", StringComparison.Ordinal));
        Assert.Contains(findings, f => f is { CostType: "betrkv-02", Severity: FindingSeverity.Warning } && f.Text.Contains("keine Kosten erfasst", StringComparison.Ordinal));
        Assert.Contains(findings, f => f is { CostType: "betrkv-13", Severity: FindingSeverity.Info });
    }

    [Fact]
    public void Threshold_is_configurable()
    {
        var previous = Calculate(Period.CalendarYear(2025), [Cost2025(1, "betrkv-08", 100m)]);
        var current = Calculate(Year2026, [Cost(1, "betrkv-08", 110m)]);

        Assert.DoesNotContain(PlausibilityChecker.Check(current, previous, 20m), f => f.Step == WizardStep.Costs);
        Assert.Single(PlausibilityChecker.Check(current, previous, 5m), f => f.Step == WizardStep.Costs);
    }

    [Fact]
    public void Cost_type_that_is_not_agreed_in_the_lease_is_a_warning()
    {
        var current = Calculate(
            Year2026,
            [Cost(1, "betrkv-01", 1000m)],
            [Lease(1, "Anna", 1, D(2024, 1, 1), excluded: "betrkv-01"), Lease(2, "Ben", 2, D(2024, 1, 1))]);

        var findings = PlausibilityChecker.Check(current, null);

        Assert.Contains(findings, f => f.Severity == FindingSeverity.Warning && f.Text.StartsWith("Anna:", StringComparison.Ordinal) && f.Text.Contains("nicht vereinbart", StringComparison.Ordinal));
    }

    [Fact]
    public void Prepayments_below_target_are_a_warning()
    {
        var current = Calculate(
            Year2026,
            [Cost(1, "betrkv-01", 1000m)],
            [Lease(1, "Anna", 1, D(2024, 1, 1), prepayments: [Prepayment(2026, 1, 50m, 50m), Prepayment(2026, 2, 50m, 0m)]), Lease(2, "Ben", 2, D(2024, 1, 1))]);

        var findings = PlausibilityChecker.Check(current, null);

        Assert.Contains(findings, f => f.Text.Contains("Anna: Vorauszahlungen Betriebskosten gezahlt 50,00 € statt Soll 100,00 €", StringComparison.Ordinal));
    }

    [Fact]
    public void Calculation_problems_are_taken_over_with_their_severity()
    {
        // The lease has no person count for the persons key → error from the core.
        var current = Calculate(
            Year2026,
            [Cost(1, "betrkv-01", 1000m)],
            rules: [Rule("betrkv-01", AllocationKey.LivingArea), Rule("betrkv-02", AllocationKey.LivingArea)]);
        var withError = current with { Problems = [new CalculationProblem(ProblemSeverity.Error, "Es fehlt etwas."), new CalculationProblem(ProblemSeverity.Warning, "Hinweis.")] };

        var findings = PlausibilityChecker.Check(withError, null);

        Assert.Contains(findings, f => f is { Severity: FindingSeverity.Error, Step: WizardStep.Calculation } && f.Text == "Es fehlt etwas.");
        Assert.Contains(findings, f => f is { Severity: FindingSeverity.Warning, Step: WizardStep.Calculation } && f.Text == "Hinweis.");
    }

    [Fact]
    public void Clean_statement_has_no_findings()
    {
        var current = Calculate(Year2026, [Cost(1, "betrkv-01", 1000m)]);

        Assert.DoesNotContain(PlausibilityChecker.Check(current, null), f => f.Severity != FindingSeverity.Info);
    }
}
