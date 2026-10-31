using Elocaro.Domain.Common;
using Elocaro.Domain.OperatingCosts;
using Elocaro.Domain.Statements;
using static Elocaro.Domain.Tests.OperatingCosts.Scenario;

namespace Elocaro.Domain.Tests.Statements;

public class StatementExplanationTests
{
    private static BillingResult Calculate(Period period, decimal grundsteuer, decimal? versicherung = null, DateOnly? annaStart = null)
    {
        var costs = new List<BillingCost> { Cost(1, "betrkv-01", grundsteuer, period) };
        if (versicherung is { } amount)
        {
            costs.Add(Cost(2, "betrkv-13", amount, period));
        }

        return OperatingCostCalculator.Calculate(Input(
            [Unit(1, "Wohnung 1", 60m), Unit(2, "Wohnung 2", 40m)],
            [Lease(1, "Anna", 1, annaStart ?? D(2024, 1, 1)), Lease(2, "Ben", 2, D(2024, 1, 1))],
            costs,
            [Rule("betrkv-01", AllocationKey.LivingArea), Rule("betrkv-13", AllocationKey.LivingArea)],
            period: period));
    }

    [Fact]
    public void Summary_names_total_key_and_share_from_the_frozen_line()
    {
        var line = Line(Calculate(Year2026, 1240m), 1, "betrkv-01");

        var summary = StatementExplainer.Summary(line);

        Assert.Equal($"1.240,00 € nach {line.Key}: Ihr Anteil 744,00 €", summary);
        Assert.Contains("Wohnfläche", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Details_show_the_share_of_the_key_and_what_is_not_charged()
    {
        var line = Line(Calculate(Year2026, 1000m), 1, "betrkv-01");

        var details = StatementExplainer.Details(line);
        var notAgreed = StatementExplainer.Details(line with { NotAgreed = 25m });

        Assert.Equal($"Ihr Anteil am Schlüssel: {line.Basis}", Assert.Single(details));
        Assert.Contains(notAgreed, d => d.Contains("nicht vereinbart", StringComparison.Ordinal) && d.Contains("25,00 €", StringComparison.Ordinal));
    }

    [Fact]
    public void A_line_without_basis_and_key_omits_those_sentences()
    {
        var line = Line(Calculate(Year2026, 1000m), 1, "betrkv-01") with { Basis = "", Key = "" };

        Assert.Empty(StatementExplainer.Details(line));
        Assert.DoesNotContain(" nach ", StatementExplainer.Summary(line), StringComparison.Ordinal);
    }

    [Fact]
    public void Question_subject_names_the_year_and_optionally_the_position()
    {
        Assert.Equal("Frage zur Abrechnung 2026", StatementExplainer.QuestionSubject(Year2026));
        Assert.Equal("Frage zur Abrechnung 2026: Grundsteuer", StatementExplainer.QuestionSubject(Year2026, "Grundsteuer"));
        Assert.Equal("Frage zur Abrechnung 01.04.2026–31.03.2027", StatementExplainer.QuestionSubject(new Period(D(2026, 4, 1), D(2027, 3, 31))));
        Assert.True(StatementExplainer.QuestionSubject(Year2026, new string('x', 500)).Length <= 200);
    }

    [Fact]
    public void Comparison_lists_new_and_dropped_cost_types_with_the_difference()
    {
        var previous = Statement(Calculate(Period.CalendarYear(2025), 500m, 100m), 1);
        var current = Statement(Calculate(Year2026, 600m), 1);

        var comparison = StatementComparer.Compare(Period.CalendarYear(2025), current, previous);

        var tax = comparison.Lines.Single(l => l.CostType == "betrkv-01");
        Assert.Equal((300m, 360m, 60m), (tax.Previous, tax.Current, tax.Difference));
        var insurance = comparison.Lines.Single(l => l.CostType == "betrkv-13");
        Assert.Equal((60m, null, -60m), (insurance.Previous, insurance.Current, insurance.Difference));
        Assert.Equal(0m, comparison.Difference);
        Assert.False(comparison.TermDiffers);
    }

    [Fact]
    public void A_different_tenancy_length_is_reported()
    {
        var previous = Statement(Calculate(Period.CalendarYear(2025), 500m, annaStart: D(2025, 7, 1)), 1);
        var current = Statement(Calculate(Year2026, 600m), 1);

        var comparison = StatementComparer.Compare(Period.CalendarYear(2025), current, previous);

        Assert.True(comparison.TermDiffers);
        Assert.Equal((184, 365), (comparison.PreviousDays, comparison.Days));
    }
}
