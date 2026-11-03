using System.Security.Claims;

namespace Elocaro.Application.Todos;

/// <summary>
/// Provides open tasks for the "Was ist zu tun?" dashboard. Each business module registers its own source
/// (open items, deadlines, tickets, unassigned transactions ...); the dashboard collects them all.
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

/// <param name="Area">Grouping on the dashboard, e.g. "Finanzen".</param>
/// <param name="Title">Short and action-oriented, e.g. "3 Umsätze zuordnen".</param>
/// <param name="Link">Page on which the task is completed.</param>
/// <param name="Due">Optional due date (e.g. statement deadline).</param>
/// <param name="Progress">Optional state of the checklist the task belongs to.</param>
public sealed record TodoItem(string Area, string Title, string Link, Urgency Urgency, string? Details = null, DateOnly? Due = null, TodoProgress? Progress = null);

public sealed record TodoProgress(int Done, int Total);

public static class TodoAreas
{
    /// <summary>The first steps of a new installation; the dashboard shows them apart from the daily work.</summary>
    public const string Setup = "So geht es los";
}
