using System.Globalization;

namespace Elocaro.Web.Components.Controls;

/// <summary>Maps amounts onto a chart's percentage scale. Negative amounts (credits) stay at the start of the scale; the text keeps the real amount.</summary>
public static class ChartScale
{
    /// <summary>Largest positive amount, or 0 if there is nothing a bar could show.</summary>
    public static decimal Max(IEnumerable<decimal> amounts) => amounts.Where(a => a > 0m).DefaultIfEmpty(0m).Max();

    /// <summary>Position of the amount in percent, between 0 and 100. A scale without a positive maximum yields 0.</summary>
    public static decimal Percent(decimal amount, decimal max) => max <= 0m ? 0m : Math.Clamp(amount / max * 100m, 0m, 100m);

    /// <summary>A percentage as a CSS value.</summary>
    public static string Css(decimal percent) => percent.ToString("0.##", CultureInfo.InvariantCulture);
}
