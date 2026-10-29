using System.Globalization;

namespace Elocaro.Domain.OperatingCosts;

/// <summary>Readable share text for the statement ("70,7 von 263,7 m² (Ø 365 Tage) = 26,82 %").</summary>
public static class ShareText
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>
    /// Real quantities (consumption): "1.634 von 7.000 kWh = 23,34 %".
    /// </summary>
    public static string Quantity(decimal weight, decimal total, string unit) =>
        $"{Number(weight, "#,##0.###")} von {Number(total, "#,##0.###")} {unit}{Percent(weight, total)}";

    /// <summary>
    /// Weights accumulated per day (area × days, person days, ...): divided by <paramref name="days"/> to the average size
    /// in the period, e.g. 25.814 of 96.236 m²·days over 365 days → "70,7 von 263,7 m² (Ø 365 Tage) = 26,82 %".
    /// </summary>
    public static string PerDay(decimal weight, decimal total, int days, string unit) =>
        days <= 0
            ? Quantity(weight, total, unit)
            : $"{Number(weight / days, "#,##0.##")} von {Number(total / days, "#,##0.##")} {unit} (Ø {days} Tage){Percent(weight, total)}";

    private static string Percent(decimal weight, decimal total) =>
        total > 0m ? $" = {(weight / total * 100m).ToString("#,##0.00", German)} %" : string.Empty;

    private static string Number(decimal value, string format) => value.ToString(format, German);
}
