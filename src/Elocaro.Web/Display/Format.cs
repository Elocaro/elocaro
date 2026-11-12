using System.Globalization;
using Elocaro.Domain.Common;

namespace Elocaro.Web.Display;

/// <summary>Formatting for the UI (German time and notation).</summary>
public static class Format
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    public static string Timestamp(DateTimeOffset? value) =>
        value is null ? "–" : TimeZoneInfo.ConvertTime(value.Value, GermanTime.Germany).ToString("dd.MM.yyyy HH:mm", German);

    /// <summary>Date of a point in time in German local time (without the time of day).</summary>
    public static string Day(DateTimeOffset? value) =>
        value is null ? "–" : TimeZoneInfo.ConvertTime(value.Value, GermanTime.Germany).ToString("dd.MM.yyyy", German);

    public static string Date(DateOnly? value) => value?.ToString("dd.MM.yyyy", German) ?? "–";

    /// <summary>Month and year in words, e.g. "Januar 2026".</summary>
    public static string Month(DateOnly? value) => value?.ToString("MMMM yyyy", German) ?? "–";

    public static string Money(decimal? value) => value?.ToString("#,##0.00 €", German) ?? "–";

    /// <summary>A difference with its sign, e.g. "+27,29 €" or "−3,00 €"; no difference has no sign.</summary>
    public static string SignedMoney(decimal difference) =>
        difference == 0 ? Money(0m) : (difference > 0 ? "+" : "−") + Money(Math.Abs(difference));

    /// <summary>Working time as "2 h 05 min"; no time = a dash.</summary>
    /// <summary>File size in B, KB, MB, GB or TB with German decimal comma ("3,6 GB"); a dash if unknown.</summary>
    public static string Size(long? bytes)
    {
        if (bytes is not { } value)
        {
            return "–";
        }

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var size = (double)value;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return unit == 0 ? $"{value} B" : size.ToString(size >= 100 ? "#,##0" : "#,##0.0", German) + " " + units[unit];
    }

    public static string Hours(int minutes) => minutes == 0 ? "–" : $"{minutes / 60} h {minutes % 60:00} min";

    public static string Area(decimal? value) => value is null ? "–" : value.Value.ToString("#,##0.##", German) + " m²";

    public static string Number(decimal? value) => value?.ToString("#,##0.##", German) ?? "–";

    /// <summary>Meter readings/quantities with up to three decimal places.</summary>
    public static string Quantity(decimal? value, string? unit = null) =>
        value is null ? "–" : value.Value.ToString("#,##0.###", German) + (unit is null ? "" : " " + unit);

    public static string Period(Domain.Common.Period period) =>
        period.To is { } to ? $"{Date(period.From)} – {Date(to)}" : $"seit {Date(period.From)}";
}
