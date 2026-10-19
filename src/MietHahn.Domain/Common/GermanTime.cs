namespace MietHahn.Domain.Common;

/// <summary>Fachliche Zeitzone: Fristen, Stichtage und Datumsanzeigen beziehen sich auf deutsche Zeit.</summary>
public static class GermanTime
{
    public static readonly TimeZoneInfo Germany = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    /// <summary>Kalendertag in Deutschland zum gegebenen Zeitpunkt.</summary>
    public static DateOnly Today(DateTimeOffset now) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, Germany).DateTime);
}
