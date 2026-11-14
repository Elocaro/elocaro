using Elocaro.Domain.Portfolio;

namespace Elocaro.Domain.Tests.Portfolio;

public class RentPotentialTests
{
    private static readonly DateOnly Today = new(2027, 2, 10);

    private static RentPotentialInput Lease(decimal rent, decimal? area, decimal threeYearsAgo, int cap = 20, DateOnly? earliest = null, bool standard = true) =>
        new(Guid.NewGuid(), "Wohnung 1", rent, area, threeYearsAgo, cap, earliest ?? new DateOnly(2026, 1, 1), standard);

    [Fact]
    public void The_comparative_rent_is_the_limit_when_it_lies_below_the_cap_limit()
    {
        // 60 m² × 12 €/m² = 720 €; cap limit 600 € + 20 % = 720 € (not below): the comparative rent is reached exactly
        var row = RentPotentialCalculator.Assess(Lease(600m, 60m, 600m), 12m, Today);

        Assert.Equal(720m, row.ComparativeRent);
        Assert.Equal(720m, row.CapLimit);
        Assert.Equal(720m, row.MaxRent);
        Assert.Equal(120m, row.Potential);
        Assert.Equal(RentPotentialLimit.ComparativeRent, row.Limit);
        Assert.Equal(10m, row.CurrentPerSquareMetre);
    }

    [Fact]
    public void The_cap_limit_holds_the_rent_back_when_the_comparative_rent_is_higher()
    {
        // 80 m² × 10 €/m² = 800 €; cap limit 500 € + 20 % = 600 €
        var row = RentPotentialCalculator.Assess(Lease(500m, 80m, 500m), 10m, Today);

        Assert.Equal(800m, row.ComparativeRent);
        Assert.Equal(600m, row.MaxRent);
        Assert.Equal(100m, row.Potential);
        Assert.Equal(RentPotentialLimit.CapLimit, row.Limit);
    }

    [Fact]
    public void A_tight_market_lowers_the_cap_limit_to_15_percent()
    {
        var row = RentPotentialCalculator.Assess(Lease(500m, 80m, 500m, cap: 15), 10m, Today);

        Assert.Equal(575m, row.CapLimit);
        Assert.Equal(75m, row.Potential);
    }

    [Fact]
    public void A_rent_at_or_above_the_comparative_rent_has_no_potential()
    {
        var row = RentPotentialCalculator.Assess(Lease(700m, 50m, 600m), 12m, Today);

        Assert.Equal(600m, row.ComparativeRent);
        Assert.Equal(0m, row.Potential);
        Assert.Equal(RentPotentialLimit.NoPotential, row.Limit);
    }

    [Fact]
    public void The_earliest_declaration_is_never_in_the_past_and_sets_the_effective_date()
    {
        var past = RentPotentialCalculator.Assess(Lease(500m, 80m, 500m, earliest: new DateOnly(2026, 1, 1)), 10m, Today);
        Assert.Equal(Today, past.EarliestDeclaration);
        Assert.Equal(new DateOnly(2027, 5, 1), past.EffectiveFrom); // start of the third month after receipt

        var waiting = RentPotentialCalculator.Assess(Lease(500m, 80m, 500m, earliest: new DateOnly(2027, 8, 20)), 10m, Today);
        Assert.Equal(new DateOnly(2027, 8, 20), waiting.EarliestDeclaration);
        Assert.Equal(new DateOnly(2027, 11, 1), waiting.EffectiveFrom);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(0, true)]
    [InlineData(60, false)]
    public void Without_a_living_area_or_for_index_and_graduated_rents_nothing_is_assessed(int? area, bool standard)
    {
        var row = RentPotentialCalculator.Assess(Lease(600m, area, 600m, standard: standard), 12m, Today);

        Assert.Equal(RentPotentialLimit.NotAssessable, row.Limit);
        Assert.Equal(0m, row.Potential);
        Assert.Null(row.ComparativeRent);
    }

    [Fact]
    public void The_calculation_is_written_out()
    {
        var row = RentPotentialCalculator.Assess(Lease(500m, 80m, 500m), 10m, Today);

        Assert.Contains("10,00 €/m² × 80 m² = 800,00 €", row.Calculation, StringComparison.Ordinal);
        Assert.Contains("Kappungsgrenze 500,00 € + 20 % = 600,00 €", row.Calculation, StringComparison.Ordinal);
    }
}
