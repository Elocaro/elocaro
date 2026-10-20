using System.Globalization;
using MietHahn.Domain.Common;

namespace MietHahn.Domain.Tests.Common;

public class GermanTextTests
{
    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("")] // invariant, as in a container without locale
    public void Amounts_are_written_in_German_notation_whatever_the_culture_of_the_host(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);

            Assert.Equal("1.234,50 €", GermanText.Money(1234.5m));
            Assert.Equal("238,00 €", GermanText.Money(238m));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
