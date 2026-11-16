using Elocaro.Domain.Prospects;

namespace Elocaro.Domain.Tests.Prospects;

public class NewLetRentTests
{
    // 60 m² at 10 €/m² comparative rent: limit 660 €.
    [Theory]
    [InlineData(660, null, false)]
    [InlineData(600, null, false)]
    [InlineData(660.01, null, true)]
    [InlineData(750, 700.0, true)]
    [InlineData(700, 700.0, false)]
    [InlineData(700, 750.0, false)]
    public void The_rent_brake_limit_is_the_comparative_rent_plus_ten_percent_and_the_previous_rent_covers_more(double rent, double? previous, bool over)
    {
        var check = NewLetRent.Check((decimal)rent, 60m, 10m, (decimal?)previous, 20);

        Assert.Equal(660m, check.Limit);
        Assert.Equal(over, check.OverLimit);
    }

    [Fact]
    public void Without_comparative_rent_or_area_it_cannot_be_checked_and_says_so()
    {
        var check = NewLetRent.Check(900m, null, 10m, null, 20);

        Assert.Equal((null, false), (check.Limit, check.OverLimit));
        Assert.Contains(check.Hints, h => h.Contains("lässt sich nicht prüfen", StringComparison.Ordinal));
    }

    [Fact]
    public void The_hints_always_mention_the_ordinance_and_the_cap_limit_of_the_property()
    {
        var check = NewLetRent.Check(600m, 60m, 10m, null, 15);

        Assert.Contains(check.Hints, h => h.Contains("Verordnung", StringComparison.Ordinal));
        Assert.Contains(check.Hints, h => h.Contains("15 %", StringComparison.Ordinal));
    }
}
