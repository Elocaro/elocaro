using Elocaro.Domain.Common;
using Elocaro.Domain.Portfolio;

namespace Elocaro.Domain.Tests.Portfolio;

public class PropertyKeyFiguresTests
{
    // Golden master: 4 flats, rent 24,000 € net, 4,200 € maintenance and administration, 9,000 € passed-through costs,
    // 6,000 € interest, 2,000 € repayment, purchase price 300,000 € plus 30,000 € ancillary costs.
    private static PropertyFigureInput Input() => new(
        Year: 2026,
        BaseRent: 24_000m,
        TotalIncome: 33_000m,
        NonAllocableCosts: 4_200m,
        AllocableCosts: 9_000m,
        Interest: 6_000m,
        OneOffFinancingCosts: 500m,
        Repayment: 2_000m,
        Investment: 330_000m,
        RentableUnitDays: 4 * 365,
        VacantUnitDays: 73,
        DueInYear: 33_000m,
        ArrearsOfYear: 660m);

    [Fact]
    public void Figures_follow_the_calculation()
    {
        var figures = PropertyKeyFigureCalculator.Calculate(Input());

        Assert.Equal(6.00m, figures.NetYield); // (24,000 - 4,200) / 330,000
        Assert.Equal(33_000m - (4_200m + 9_000m + 6_000m + 500m), figures.CashflowBeforeRepayment); // 13,300
        Assert.Equal(11_300m, figures.Cashflow);
        Assert.Equal(5.0m, figures.VacancyPercent); // 73 / 1,460
        Assert.Equal(2.0m, figures.ArrearsPercent);
    }

    [Fact]
    public void The_way_to_each_figure_is_listed()
    {
        var steps = PropertyKeyFigureCalculator.Calculate(Input()).Steps;

        Assert.Contains(steps, s => s.Label == "Nettomietrendite (%)" && s.Formula!.Contains("Kaufnebenkosten", StringComparison.Ordinal));
        Assert.Equal(["Nettomiete", "Nettomietrendite (%)", "Einnahmen", "Ausgaben ohne AfA", "Cashflow vor Tilgung", "Tilgung", "Cashflow", "Leerstandsquote (%)", "Rückstandsquote (%)"],
            steps.Select(s => s.Label));
    }

    [Fact]
    public void Without_investment_units_or_claims_the_dependent_figures_are_empty()
    {
        var figures = PropertyKeyFigureCalculator.Calculate(Input() with { Investment = null, RentableUnitDays = 0, VacantUnitDays = 0, DueInYear = 0m, ArrearsOfYear = 0m });

        Assert.Null(figures.NetYield);
        Assert.Null(figures.VacancyPercent);
        Assert.Null(figures.ArrearsPercent);
        Assert.Equal(11_300m, figures.Cashflow);
    }

    [Fact]
    public void Vacant_days_are_the_year_minus_the_union_of_the_leases()
    {
        // Lease 1: 1 Jan to 31 Mar, lease 2: 15 Mar (overlap) to 30 Jun, lease 3 open-ended from 1 Oct.
        var periods = new[]
        {
            new Period(new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31)),
            new Period(new DateOnly(2026, 3, 15), new DateOnly(2026, 6, 30)),
            new Period(new DateOnly(2026, 10, 1)),
        };

        // Vacant: 1 Jul to 30 Sep = 92 days.
        Assert.Equal(92, PropertyKeyFigureCalculator.VacantDays(2026, periods));
        Assert.Equal(365, PropertyKeyFigureCalculator.VacantDays(2026, []));
        Assert.Equal(0, PropertyKeyFigureCalculator.VacantDays(2026, [new Period(new DateOnly(2020, 1, 1))]));
        Assert.Equal(365, PropertyKeyFigureCalculator.VacantDays(2026, [new Period(new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31))]));
    }
}
