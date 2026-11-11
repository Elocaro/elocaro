using System.Globalization;

namespace Elocaro.TestHelpers;

/// <summary>
/// Converts the double literals of <c>[InlineData]</c> to the decimal the literal was written as. Since .NET 11 the cast
/// <c>(decimal)503.33</c> is exact (503,3299999999999840…) instead of rounding to 15 significant digits.
/// </summary>
public static class Dec
{
    public static decimal From(double value) =>
        decimal.Parse(value.ToString("R", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture);
}
