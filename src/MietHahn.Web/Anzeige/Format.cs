using System.Globalization;
using MietHahn.Domain.Gemeinsam;

namespace MietHahn.Web.Anzeige;

/// <summary>Formatierung für die Oberfläche (deutsche Zeit und Schreibweise).</summary>
public static class Format
{
    private static readonly CultureInfo Deutsch = CultureInfo.GetCultureInfo("de-DE");

    public static string Zeitpunkt(DateTimeOffset? wert) =>
        wert is null ? "–" : TimeZoneInfo.ConvertTime(wert.Value, Zeitzone.Deutschland).ToString("dd.MM.yyyy HH:mm", Deutsch);

    public static string Datum(DateOnly? wert) => wert?.ToString("dd.MM.yyyy", Deutsch) ?? "–";

    public static string Geld(decimal? wert) => wert?.ToString("#,##0.00 €", Deutsch) ?? "–";

    public static string Flaeche(decimal? wert) => wert is null ? "–" : wert.Value.ToString("#,##0.##", Deutsch) + " m²";

    public static string Zahl(decimal? wert) => wert?.ToString("#,##0.##", Deutsch) ?? "–";

    /// <summary>Zählerstände/Mengen mit bis zu drei Nachkommastellen.</summary>
    public static string Menge(decimal? wert, string? einheit = null) =>
        wert is null ? "–" : wert.Value.ToString("#,##0.###", Deutsch) + (einheit is null ? "" : " " + einheit);

    public static string Zeitraum(Domain.Gemeinsam.Zeitraum zeitraum) =>
        zeitraum.Bis is { } bis ? $"{Datum(zeitraum.Von)} – {Datum(bis)}" : $"seit {Datum(zeitraum.Von)}";
}
