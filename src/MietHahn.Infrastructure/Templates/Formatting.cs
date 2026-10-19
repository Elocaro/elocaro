using System.Globalization;
using MietHahn.Domain.Common;

namespace MietHahn.Infrastructure.Templates;

/// <summary>Einheitliche deutsche Formatierung für Dokumente und Mails.</summary>
public static class Formatting
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>1234.5 → „1.234,50 €“.</summary>
    public static string Money(decimal? amount) => amount?.ToString("#,##0.00 €", German) ?? "";

    /// <summary>Datum im Format „TT.MM.JJJJ“ (DateOnly, DateTime, DateTimeOffset in deutscher Zeit).</summary>
    public static string Date(object? value) => value switch
    {
        null => "",
        DateOnly d => d.ToString("dd.MM.yyyy", German),
        DateTimeOffset dto => TimeZoneInfo.ConvertTime(dto, GermanTime.Germany).ToString("dd.MM.yyyy", German),
        DateTime dt => dt.ToString("dd.MM.yyyy", German),
        _ => throw new ArgumentException($"Kein Datumswert: {value.GetType().Name}", nameof(value)),
    };
}
