using Elocaro.Domain.OperatingCosts;
using Elocaro.Domain.Statements;
using static Elocaro.Domain.Tests.OperatingCosts.Scenario;

namespace Elocaro.Domain.Tests.Statements;

public class ObjectionCheckerTests
{
    private static BillingResult Calculate(params BillingCost[] costs) =>
        OperatingCostCalculator.Calculate(Input(
            [Unit(1, "Wohnung 1", 60m)],
            [Lease(1, "Anna", 1, D(2024, 1, 1))],
            costs,
            [Rule("betrkv-01", AllocationKey.LivingArea), Rule("betrkv-13", AllocationKey.LivingArea)],
            period: Year2026));

    [Fact]
    public void Everything_documented_gives_no_note()
    {
        var result = Calculate(Cost(1, "betrkv-01", 600m, Year2026));

        Assert.Empty(ObjectionChecker.Check(result, new HashSet<Guid> { Id(1) }));
    }

    [Fact]
    public void A_cost_type_without_any_receipt_file_is_a_note_not_a_blocker()
    {
        var result = Calculate(Cost(1, "betrkv-01", 600m, Year2026), Cost(2, "betrkv-01", 100m, Year2026));

        var finding = Assert.Single(ObjectionChecker.Check(result, new HashSet<Guid>()));

        Assert.Equal((WizardStep.Costs, FindingSeverity.Info, "betrkv-01"), (finding.Step, finding.Severity, finding.CostType));
        Assert.Contains("Prüfhinweis", finding.Text, StringComparison.Ordinal);
        Assert.Contains("2 Positionen (700,00 €)", finding.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Single_undocumented_positions_are_named()
    {
        var result = Calculate(Cost(1, "betrkv-01", 600m, Year2026, description: "Stadt Köln"), Cost(2, "betrkv-01", 100m, Year2026, description: "Nachtrag"));

        var finding = Assert.Single(ObjectionChecker.Check(result, new HashSet<Guid> { Id(1) }));

        Assert.Contains("1 von 2 Positionen (100,00 €)", finding.Text, StringComparison.Ordinal);
        Assert.Contains("Nachtrag", finding.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Stadt Köln", finding.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Items_marked_as_not_allocable_are_noted_once()
    {
        var result = Calculate(Cost(1, "betrkv-01", 600m, Year2026), Cost(2, "betrkv-13", 80m, Year2026, allocable: false));

        var findings = ObjectionChecker.Check(result, new HashSet<Guid> { Id(1), Id(2) });

        var finding = Assert.Single(findings);
        Assert.Equal(FindingSeverity.Info, finding.Severity);
        Assert.Contains("1 Position (80,00 €) sind als nicht umlagefähig markiert", finding.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void No_note_ever_blocks_or_needs_acknowledgement()
    {
        var result = Calculate(Cost(1, "betrkv-01", 600m, Year2026), Cost(2, "betrkv-13", 80m, Year2026, allocable: false));

        Assert.All(ObjectionChecker.Check(result, new HashSet<Guid>()), f => Assert.Equal(FindingSeverity.Info, f.Severity));
    }
}
