using System.Globalization;
using MietHahn.Domain.Common;

namespace MietHahn.Web.Display;

/// <summary>Formatierung für die Oberfläche (deutsche Zeit und Schreibweise).</summary>
public static class Format
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    public static string Timestamp(DateTimeOffset? value) =>
        value is null ? "–" : TimeZoneInfo.ConvertTime(value.Value, GermanTime.Germany).ToString("dd.MM.yyyy HH:mm", German);

    public static string Date(DateOnly? value) => value?.ToString("dd.MM.yyyy", German) ?? "–";

    public static string Money(decimal? value) => value?.ToString("#,##0.00 €", German) ?? "–";

    public static string Area(decimal? value) => value is null ? "–" : value.Value.ToString("#,##0.##", German) + " m²";

    public static string Number(decimal? value) => value?.ToString("#,##0.##", German) ?? "–";

    /// <summary>Zählerstände/Mengen mit bis zu drei Nachkommastellen.</summary>
    public static string Quantity(decimal? value, string? unit = null) =>
        value is null ? "–" : value.Value.ToString("#,##0.###", German) + (unit is null ? "" : " " + unit);

    public static string Period(Domain.Common.Period period) =>
        period.To is { } to ? $"{Date(period.From)} – {Date(to)}" : $"seit {Date(period.From)}";
}
