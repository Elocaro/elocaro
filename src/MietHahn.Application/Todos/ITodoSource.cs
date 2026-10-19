using System.Security.Claims;

namespace MietHahn.Application.Todos;

/// <summary>
/// Liefert offene Aufgaben für das Dashboard „Was ist zu tun?“. Jedes Fachmodul registriert eine eigene Quelle
/// (offene Posten, Fristen, Tickets, unzugeordnete Umsätze …); das Dashboard sammelt alle ein.
/// </summary>
public interface ITodoSource
{
    Task<IReadOnlyList<TodoItem>> TodosAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default);
}

public enum Urgency
{
    Info = 0,
    Soon = 1,
    Urgent = 2,
}

/// <param name="Area">Gruppierung im Dashboard, z. B. „Finanzen“.</param>
/// <param name="Title">Kurz und handlungsorientiert, z. B. „3 Umsätze zuordnen“.</param>
/// <param name="Link">Seite, auf der die Aufgabe erledigt wird.</param>
/// <param name="Due">Optionaler Stichtag (z. B. Abrechnungsfrist).</param>
public sealed record TodoItem(string Area, string Title, string Link, Urgency Urgency, string? Details = null, DateOnly? Due = null);
