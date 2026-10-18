using System.Security.Claims;

namespace MietHahn.Application.Aufgaben;

/// <summary>
/// Liefert offene Aufgaben für das Dashboard „Was ist zu tun?“. Jedes Fachmodul registriert eine eigene Quelle
/// (offene Posten, Fristen, Tickets, unzugeordnete Umsätze …); das Dashboard sammelt alle ein.
/// </summary>
public interface IAufgabenQuelle
{
    Task<IReadOnlyList<Aufgabe>> AufgabenAsync(ClaimsPrincipal benutzer, CancellationToken cancellationToken = default);
}

public enum Dringlichkeit
{
    Info = 0,
    Bald = 1,
    Dringend = 2,
}

/// <param name="Bereich">Gruppierung im Dashboard, z. B. „Finanzen“.</param>
/// <param name="Titel">Kurz und handlungsorientiert, z. B. „3 Umsätze zuordnen“.</param>
/// <param name="Link">Seite, auf der die Aufgabe erledigt wird.</param>
/// <param name="Faellig">Optionaler Stichtag (z. B. Abrechnungsfrist).</param>
public sealed record Aufgabe(string Bereich, string Titel, string Link, Dringlichkeit Dringlichkeit, string? Details = null, DateOnly? Faellig = null);
