using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using MietHahn.Application.Accounts;
using MietHahn.Application.Common;
using MietHahn.Domain.Deadlines;

namespace MietHahn.Application.Deadlines;

/// <summary>Where a deadline comes from.</summary>
public enum DeadlineOrigin
{
    /// <summary>Derived from data (contracts, meters, loans ...), no manual entry.</summary>
    Derived = 0,

    /// <summary>Entered maintenance task.</summary>
    Task = 1,
}

/// <param name="Key">Stable identity (for the reminder log and the calendar UID).</param>
/// <param name="Area">Grouping, e.g. "Mietverträge".</param>
/// <param name="Right">Right needed to see the entry.</param>
/// <param name="LeadDays">Own notice for the reminder; empty = default of the settings.</param>
public sealed record DeadlineEntry(
    string Key,
    DeadlineOrigin Origin,
    string Area,
    string Title,
    DateOnly Due,
    string Link,
    AccessRight Right,
    string? Details = null,
    int? LeadDays = null,
    Guid? TaskId = null);

/// <summary>Provides derived deadlines of a business module; the calendar collects all sources.</summary>
public interface IDeadlineSource
{
    /// <summary>Open deadlines up to and including <paramref name="until"/>; overdue ones are included.</summary>
    Task<IReadOnlyList<DeadlineEntry>> DeadlinesAsync(DateOnly until, CancellationToken cancellationToken = default);
}

/// <summary>Deadline calendar: derived deadlines, maintenance tasks, reminders and calendar feed.</summary>
public interface IDeadlines
{
    /// <summary>All deadlines visible to the user up to <paramref name="until"/> (overdue ones included), sorted by date.</summary>
    Task<IReadOnlyList<DeadlineEntry>> ListAsync(ClaimsPrincipal user, DateOnly until, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TaskListItem>> TasksAsync(CancellationToken cancellationToken = default);

    Task<TaskDetail?> TaskAsync(Guid taskId, CancellationToken cancellationToken = default);

    Task<Result> CreateTaskAsync(TaskInput input, CancellationToken cancellationToken = default);

    Task<Result> UpdateTaskAsync(Guid taskId, TaskInput input, CancellationToken cancellationToken = default);

    /// <summary>Marks the task as done, with the document as proof, and sets the next date.</summary>
    Task<Result> CompleteTaskAsync(Guid taskId, CompletionInput input, CancellationToken cancellationToken = default);

    Task<Result> DeleteTaskAsync(Guid taskId, CancellationToken cancellationToken = default);

    Task<DeadlineSettingsInput> SettingsAsync(CancellationToken cancellationToken = default);

    Task<Result> SaveSettingsAsync(DeadlineSettingsInput input, CancellationToken cancellationToken = default);

    /// <summary>Creates a new secret link (the old one stops working) or switches the feed off.</summary>
    Task<Result> RenewCalendarLinkAsync(bool enable, CancellationToken cancellationToken = default);

    /// <summary>Path of the secret calendar link (<c>/kalender/{token}.ics</c>), <c>null</c> if the feed is off.</summary>
    Task<string?> CalendarPathAsync(CancellationToken cancellationToken = default);

    /// <summary>The feed for a link; <c>null</c> if the token is unknown (the feed shows all deadlines of the next 12 months).</summary>
    Task<string?> CalendarAsync(string token, CancellationToken cancellationToken = default);
}

public sealed record TaskListItem(Guid Id, string Title, TaskKind Kind, Guid? PropertyId, string? PropertyName, DateOnly DueOn, int IntervalMonths, DateOnly? LastDoneOn, bool Active);

public sealed record CompletionRow(Guid Id, DateOnly DoneOn, Guid? DocumentId, string? DocumentTitle, string? Note);

public sealed record TaskDetail(TaskListItem Task, TaskInput Input, IReadOnlyList<CompletionRow> Completions);

public sealed class TaskInput
{
    [Required(ErrorMessage = "Bitte eine Bezeichnung angeben.")]
    [StringLength(200)]
    public string Title { get; set; } = "";

    public TaskKind Kind { get; set; } = TaskKind.Other;

    public Guid? PropertyId { get; set; }

    [Required(ErrorMessage = "Bitte das nächste Fälligkeitsdatum angeben.")]
    public DateOnly? DueOn { get; set; }

    [Range(0, RecurringTask.MaxIntervalMonths, ErrorMessage = "Der Abstand muss zwischen 0 (einmalig) und 120 Monaten liegen.")]
    public int IntervalMonths { get; set; } = 12;

    [Range(0, 365, ErrorMessage = "Der Vorlauf muss zwischen 0 und 365 Tagen liegen.")]
    public int? LeadDays { get; set; }

    [StringLength(2000)]
    public string? Note { get; set; }

    public bool Active { get; set; } = true;
}

public sealed class CompletionInput
{
    [Required(ErrorMessage = "Bitte das Datum der Erledigung angeben.")]
    public DateOnly? DoneOn { get; set; }

    /// <summary>Document as proof (already filed in the library).</summary>
    public Guid? DocumentId { get; set; }

    [StringLength(2000)]
    public string? Note { get; set; }
}

public sealed class DeadlineSettingsInput
{
    [Range(0, 365, ErrorMessage = "Der Vorlauf muss zwischen 0 und 365 Tagen liegen.")]
    public int DefaultLeadDays { get; set; } = DeadlineSettings.DefaultLead;

    public bool SendReminders { get; set; } = true;

    public bool SendWeeklySummary { get; set; } = true;
}
