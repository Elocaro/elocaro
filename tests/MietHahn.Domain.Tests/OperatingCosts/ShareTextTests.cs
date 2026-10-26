using MietHahn.Domain.OperatingCosts;

namespace MietHahn.Domain.Tests.OperatingCosts;

public class ShareTextTests
{
    [Fact]
    public void Area_days_are_shown_as_average_area_with_percent()
    {
        Assert.Equal("70,72 von 263,66 m² (Ø 365 Tage) = 26,82 %", ShareText.PerDay(25813.633m, 96235.9m, 365, "m²"));
    }

    [Fact]
    public void Quantities_are_shown_unchanged_with_percent()
    {
        Assert.Equal("1.634 von 7.000 kWh = 23,34 %", ShareText.Quantity(1634m, 7000m, "kWh"));
    }

    [Fact]
    public void Zero_total_omits_percent()
    {
        Assert.Equal("0 von 0 kWh", ShareText.Quantity(0m, 0m, "kWh"));
    }
}
