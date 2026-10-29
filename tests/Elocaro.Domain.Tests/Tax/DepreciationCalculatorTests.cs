using Elocaro.Domain.Tax;

namespace Elocaro.Domain.Tests.Tax;

public class DepreciationCalculatorTests
{
    private static BuildingAcquisition Building(DateOnly? date = null, int completion = 1990, decimal land = 20m) =>
        new(date ?? new DateOnly(2024, 7, 15), 300_000m, 30_000m, land, completion);

    private static DepreciationPlan Plan(BuildingAcquisition acquisition, int last, IReadOnlyList<SubsequentCost>? costs = null, IReadOnlyList<MovableAsset>? movables = null) =>
        DepreciationCalculator.Calculate(acquisition, costs ?? [], movables ?? [], last);

    [Theory]
    [InlineData(1900, 2.5)]
    [InlineData(1924, 2.5)]
    [InlineData(1925, 2)]
    [InlineData(2022, 2)]
    [InlineData(2023, 3)]
    public void Standard_rate_depends_on_completion_year(int year, double expected) =>
        Assert.Equal((decimal)expected, DepreciationCalculator.StandardRate(year));

    [Fact]
    public void Base_is_building_share_of_price_and_ancillary_costs() =>
        Assert.Equal(264_000m, DepreciationCalculator.BuildingBase(Building()));

    [Fact]
    public void Purchase_on_15_July_yields_six_twelfths_in_the_first_year()
    {
        var plan = Plan(Building(), 2025);

        // 264,000 × 2 % = 5,280 p.a.; 6/12 = 2,640
        Assert.Equal(2_640m, plan.Years[0].Building);
        Assert.Equal(5_280m, plan.Years[1].Building);
        Assert.Equal(264_000m - 2_640m - 5_280m, plan.Years[1].BookValue);
    }

    [Fact]
    public void Purchase_in_january_counts_twelve_months() =>
        Assert.Equal(5_280m, Plan(Building(new DateOnly(2024, 1, 1)), 2024).Years[0].Building);

    [Fact]
    public void Rate_override_and_special_depreciation_apply()
    {
        var acquisition = Building(new DateOnly(2024, 1, 2)) with { RateOverridePercent = 4m, SpecialRatePercent = 5m, SpecialYears = 2 };
        var plan = Plan(acquisition, 2026);

        Assert.Equal(264_000m * 0.09m, plan.Years[0].Building);
        Assert.Equal(264_000m * 0.09m, plan.Years[1].Building);
        Assert.Equal(264_000m * 0.04m, plan.Years[2].Building);
    }

    [Fact]
    public void Plan_stops_when_the_base_is_used_up()
    {
        var acquisition = Building(new DateOnly(2024, 1, 1)) with { RateOverridePercent = 50m };
        var plan = Plan(acquisition, 2027);

        Assert.Equal(132_000m, plan.Years[0].Building);
        Assert.Equal(132_000m, plan.Years[1].Building);
        Assert.Equal(0m, plan.Years[2].Building);
        Assert.Equal(0m, plan.Years[3].BookValue);
    }

    [Fact]
    public void Subsequent_production_cost_is_depreciated_with_the_building()
    {
        var plan = Plan(Building(new DateOnly(2024, 1, 1)), 2025, [new SubsequentCost(new DateOnly(2025, 7, 1), 12_000m, "Anbau", false)]);

        // 2025: 5,280 + 12,000 × 2 % × 6/12
        Assert.Equal(5_280m + 120m, plan.Years[1].Building);
    }

    [Fact]
    public void Movable_asset_is_depreciated_over_its_useful_life()
    {
        var kitchen = new MovableAsset("Einbauküche", new DateOnly(2024, 7, 1), 6_000m, 10);
        var plan = Plan(Building(), 2025, movables: [kitchen]);

        Assert.Equal(300m, plan.Years[0].Movables);
        Assert.Equal(600m, plan.Years[1].Movables);
        Assert.Equal(264_000m - 2_640m - 5_280m + 6_000m - 900m, plan.Years[1].BookValue);
    }

    [Fact]
    public void Exceeding_fifteen_percent_within_three_years_warns()
    {
        var costs = new[] { new SubsequentCost(new DateOnly(2025, 3, 1), 40_000m, "Sanierung Bäder", true) };

        // limit: 15 % of 264,000 = 39,600
        Assert.Contains("Anschaffungsnahe", Assert.Single(Plan(Building(), 2025, costs).Warnings));
    }

    [Fact]
    public void Costs_below_the_limit_or_after_three_years_do_not_warn()
    {
        Assert.Empty(Plan(Building(), 2025, [new SubsequentCost(new DateOnly(2025, 3, 1), 10_000m, "Dach", true)]).Warnings);
        Assert.Empty(Plan(Building(), 2030, [new SubsequentCost(new DateOnly(2028, 3, 1), 80_000m, "Sanierung", true)]).Warnings);
    }

    [Fact]
    public void Costs_close_to_the_limit_give_a_hint() =>
        Assert.Contains("nahe", Assert.Single(Plan(Building(), 2025, [new SubsequentCost(new DateOnly(2025, 3, 1), 35_000m, "Bad", true)]).Warnings));
}
