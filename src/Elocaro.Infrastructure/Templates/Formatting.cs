using System.Globalization;
using Elocaro.Domain.Common;

namespace Elocaro.Infrastructure.Templates;

/// <summary>Consistent German formatting for documents and emails.</summary>
public static class Formatting
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>1234.5 → „1.234,50 €“.</summary>
    public static string Money(decimal? amount) => amount?.ToString("#,##0.00 €", German) ?? "";

    /// <summary>Date in the format "dd.MM.yyyy" (DateOnly, DateTime, DateTimeOffset in German time).</summary>
    public static string Date(object? value) => value switch
    {
        null => "",
        DateOnly d => d.ToString("dd.MM.yyyy", German),
        DateTimeOffset dto => TimeZoneInfo.ConvertTime(dto, GermanTime.Germany).ToString("dd.MM.yyyy", German),
        DateTime dt => dt.ToString("dd.MM.yyyy", German),
        _ => throw new ArgumentException($"Kein Datumswert: {value.GetType().Name}", nameof(value)),
    };
}
