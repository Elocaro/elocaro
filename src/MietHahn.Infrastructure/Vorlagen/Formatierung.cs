using System.Globalization;
using MietHahn.Domain.Gemeinsam;

namespace MietHahn.Infrastructure.Vorlagen;

/// <summary>Einheitliche deutsche Formatierung für Dokumente und Mails.</summary>
public static class Formatierung
{
    private static readonly CultureInfo Deutsch = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>1234.5 → „1.234,50 €“.</summary>
    public static string Geld(decimal? betrag) => betrag?.ToString("#,##0.00 €", Deutsch) ?? "";

    /// <summary>Datum im Format „TT.MM.JJJJ“ (DateOnly, DateTime, DateTimeOffset in deutscher Zeit).</summary>
    public static string Datum(object? wert) => wert switch
    {
        null => "",
        DateOnly d => d.ToString("dd.MM.yyyy", Deutsch),
        DateTimeOffset dto => TimeZoneInfo.ConvertTime(dto, Zeitzone.Deutschland).ToString("dd.MM.yyyy", Deutsch),
        DateTime dt => dt.ToString("dd.MM.yyyy", Deutsch),
        _ => throw new ArgumentException($"Kein Datumswert: {wert.GetType().Name}", nameof(wert)),
    };
}
