namespace MietHahn.Domain.Gemeinsam;

/// <summary>Fachliche Zeitzone: Fristen, Stichtage und Datumsanzeigen beziehen sich auf deutsche Zeit.</summary>
public static class Zeitzone
{
    public static readonly TimeZoneInfo Deutschland = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    /// <summary>Kalendertag in Deutschland zum gegebenen Zeitpunkt.</summary>
    public static DateOnly Heute(DateTimeOffset jetzt) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(jetzt, Deutschland).DateTime);
}
