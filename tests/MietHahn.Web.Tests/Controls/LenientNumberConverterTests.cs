using MietHahn.Web.Components.Controls;

namespace MietHahn.Web.Tests.Controls;

public class LenientNumberConverterTests
{
    [Theory]
    [InlineData("0.090527", 0.090527)]
    [InlineData("0,090527", 0.090527)]
    [InlineData("1.234,56", 1234.56)]
    [InlineData("1,234.56", 1234.56)]
    [InlineData("1.000", 1000)]
    [InlineData("12.5", 12.5)]
    [InlineData("12,50 €", 12.5)]
    [InlineData("1.234.567", 1234567)]
    [InlineData("-3,5", -3.5)]
    public void Point_and_comma_are_read_as_decimal_or_thousands_sign_where_plausible(string typed, double expected)
    {
        var converter = new LenientNumberConverter<decimal>();
        Assert.Equal((decimal)expected, converter.ConvertBack(typed));
    }

    [Fact]
    public void Text_that_is_no_number_is_an_error_and_whole_numbers_reject_fractions()
    {
        Assert.Throws<FormatException>(() => new LenientNumberConverter<decimal>().ConvertBack("abc"));
        Assert.Throws<FormatException>(() => new LenientNumberConverter<int>().ConvertBack("2,5"));
        Assert.Equal(2026, new LenientNumberConverter<int>().ConvertBack("2026"));
        Assert.Null(new LenientNumberConverter<decimal?>().ConvertBack(" "));
    }
}
