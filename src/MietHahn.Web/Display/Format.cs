using System.Globalization;
using MietHahn.Domain.Common;

namespace MietHahn.Web.Display;

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

    public static string Area(decimal? value) => value is null ? "–" : value.Value.ToString("#,##0.##", German) + " m²";

    public static string Number(decimal? value) => value?.ToString("#,##0.##", German) ?? "–";

    /// <summary>Meter readings/quantities with up to three decimal places.</summary>
    public static string Quantity(decimal? value, string? unit = null) =>
        value is null ? "–" : value.Value.ToString("#,##0.###", German) + (unit is null ? "" : " " + unit);

    public static string Period(Domain.Common.Period period) =>
        period.To is { } to ? $"{Date(period.From)} – {Date(to)}" : $"seit {Date(period.From)}";
}
