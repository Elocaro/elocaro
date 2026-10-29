using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Elocaro.Application.Accounts;
using Elocaro.Application.Admin;
using Elocaro.Application.Common;
using Elocaro.Application.Communication;
using Elocaro.Application.Deadlines;
using Elocaro.Application.Todos;
using Elocaro.Domain.Common;
using Elocaro.Domain.Deadlines;
using Elocaro.Domain.Documents;
using Elocaro.Domain.Properties;
using Elocaro.Infrastructure.Common;
using Elocaro.Infrastructure.Identity;
using Elocaro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Quartz;

namespace Elocaro.Infrastructure.Deadlines;

internal sealed class DeadlineService(
    ElocaroDbContext db,
    IEnumerable<IDeadlineSource> sources,
    IOptions<ApplicationOptions> app,
    TimeProvider clock) : IDeadlines
{
    /// <summary>The calendar shows deadlines of the next 12 months.</summary>
    private const int FeedDays = 365;

    private DateOnly Today => GermanTime.Today(clock.GetUtcNow());

    public async Task<IReadOnlyList<DeadlineEntry>> ListAsync(ClaimsPrincipal user, DateOnly until, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        var all = await AllAsync(until, cancellationToken);
        return all.Where(e => user.IsInRole(Roles.Admin) || user.HasClaim(Permissions.ClaimType, e.Right.ToString())).ToList();
    }

    public async Task<IReadOnlyList<TaskListItem>> TasksAsync(CancellationToken cancellationToken = default) =>
        await (from t in db.Set<RecurringTask>().AsNoTracking()
               join o in db.Set<Property>().AsNoTracking() on t.PropertyId equals o.Id into properties
               from o in properties.DefaultIfEmpty()
               orderby !t.Active, t.DueOn, t.Title
               select new TaskListItem(t.Id, t.Title, t.Kind, t.PropertyId, o == null ? null : o.Name, t.DueOn, t.IntervalMonths, t.LastDoneOn, t.Active))
            .ToListAsync(cancellationToken);

    public async Task<TaskDetail?> TaskAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        var task = await db.Set<RecurringTask>().AsNoTracking().SingleOrDefaultAsync(t => t.Id == taskId, cancellationToken);
        if (task is null)
        {
            return null;
        }

        var propertyName = task.PropertyId is { } propertyId
            ? await db.Set<Property>().AsNoTracking().Where(o => o.Id == propertyId).Select(o => o.Name).SingleOrDefaultAsync(cancellationToken)
            : null;
        var completions = await (from c in db.Set<TaskCompletion>().AsNoTracking()
                                 join d in db.Set<Document>().AsNoTracking() on c.DocumentId equals d.Id into documents
                                 from d in documents.DefaultIfEmpty()
                                 where c.TaskId == taskId
                                 orderby c.DoneOn descending, c.CreatedAt descending
                                 select new CompletionRow(c.Id, c.DoneOn, c.DocumentId, d == null ? null : d.Title, c.Note))
            .ToListAsync(cancellationToken);
        return new TaskDetail(
            new TaskListItem(task.Id, task.Title, task.Kind, task.PropertyId, propertyName, task.DueOn, task.IntervalMonths, task.LastDoneOn, task.Active),
            new TaskInput
            {
                Title = task.Title,
                Kind = task.Kind,
                PropertyId = task.PropertyId,
                DueOn = task.DueOn,
                IntervalMonths = task.IntervalMonths,
                LeadDays = task.LeadDays,
                Note = task.Note,
                Active = task.Active,
            },
            completions);
    }

    public Task<Result> CreateTaskAsync(TaskInput input, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(input);
            var task = new RecurringTask(input.Title, input.Kind, input.DueOn ?? throw new RuleViolationException("Bitte das nächste Fälligkeitsdatum angeben."), input.IntervalMonths);
            await ApplyAsync(task, input, cancellationToken);
            db.Add(task);
            await db.SaveChangesAsync(cancellationToken);
            return task.Id;
        });

    public Task<Result> UpdateTaskAsync(Guid taskId, TaskInput input, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(input);
            var task = await db.Set<RecurringTask>().SingleOrDefaultAsync(t => t.Id == taskId, cancellationToken) ?? throw new NotFoundException();
            task.Update(input.Title, input.Kind, input.DueOn ?? throw new RuleViolationException("Bitte das nächste Fälligkeitsdatum angeben."), input.IntervalMonths);
            await ApplyAsync(task, input, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return task.Id;
        });

    public Task<Result> CompleteTaskAsync(Guid taskId, CompletionInput input, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(input);
            var task = await db.Set<RecurringTask>().SingleOrDefaultAsync(t => t.Id == taskId, cancellationToken) ?? throw new NotFoundException();
            var doneOn = input.DoneOn ?? throw new RuleViolationException("Bitte das Datum der Erledigung angeben.");
            if (doneOn > Today)
            {
                throw new RuleViolationException("Das Datum der Erledigung liegt in der Zukunft.");
            }

            if (input.DocumentId is { } documentId && !await db.Set<Document>().AnyAsync(d => d.Id == documentId, cancellationToken))
            {
                throw new RuleViolationException("Das Dokument als Nachweis wurde nicht gefunden.");
            }

            db.Add(task.Complete(doneOn, input.DocumentId, input.Note));
            await db.SaveChangesAsync(cancellationToken);
            return task.Id;
        });

    public Task<Result> DeleteTaskAsync(Guid taskId, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var task = await db.Set<RecurringTask>().SingleOrDefaultAsync(t => t.Id == taskId, cancellationToken) ?? throw new NotFoundException();
            db.Remove(task);
            await db.SaveChangesAsync(cancellationToken);
            return task.Id;
        });

    public async Task<DeadlineSettingsInput> SettingsAsync(CancellationToken cancellationToken = default)
    {
        var settings = await SettingsOrDefaultAsync(db, cancellationToken);
        return new DeadlineSettingsInput { DefaultLeadDays = settings.DefaultLeadDays, SendReminders = settings.SendReminders, SendWeeklySummary = settings.SendWeeklySummary };
    }

    public Task<Result> SaveSettingsAsync(DeadlineSettingsInput input, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(input);
            var settings = await SettingsOrDefaultAsync(db, cancellationToken);
            if (input.DefaultLeadDays is < 0 or > 365)
            {
                throw new RuleViolationException("Der Vorlauf muss zwischen 0 und 365 Tagen liegen.");
            }

            settings.DefaultLeadDays = input.DefaultLeadDays;
            settings.SendReminders = input.SendReminders;
            settings.SendWeeklySummary = input.SendWeeklySummary;
            await db.SaveChangesAsync(cancellationToken);
            return settings.Id;
        });

    public Task<Result> RenewCalendarLinkAsync(bool enable, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var settings = await SettingsOrDefaultAsync(db, cancellationToken);
            if (enable)
            {
                settings.SetCalendarToken(Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24)));
            }
            else
            {
                settings.SwitchOffCalendar();
            }

            await db.SaveChangesAsync(cancellationToken);
            return settings.Id;
        });

    public async Task<string?> CalendarPathAsync(CancellationToken cancellationToken = default)
    {
        var token = (await SettingsOrDefaultAsync(db, cancellationToken)).CalendarToken;
        return token is null ? null : $"/kalender/{token}.ics";
    }

    public async Task<string?> CalendarAsync(string token, CancellationToken cancellationToken = default)
    {
        var stored = (await SettingsOrDefaultAsync(db, cancellationToken)).CalendarToken;
        if (string.IsNullOrEmpty(token) || stored is null
            || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(stored)))
        {
            return null;
        }

        var entries = await AllAsync(Today.AddDays(FeedDays), cancellationToken);
        return ICalendarWriter.Write(
            entries.Select(e => new CalendarEvent(e.Key + ":" + e.Due.ToString("yyyyMMdd", CultureInfo.InvariantCulture), e.Due, e.Title, e.Details, app.Value.Link(e.Link).ToString())),
            clock.GetUtcNow(),
            "Elocaro Fristen");
    }

    /// <summary>The settings record; not yet saved if it does not exist (it then lives with its defaults and is created on first save).</summary>
    internal static async Task<DeadlineSettings> SettingsOrDefaultAsync(ElocaroDbContext db, CancellationToken cancellationToken)
    {
        var settings = await db.Set<DeadlineSettings>().OrderBy(s => s.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        if (settings is null)
        {
            settings = new DeadlineSettings();
            db.Add(settings);
        }

        return settings;
    }

    internal async Task<IReadOnlyList<DeadlineEntry>> AllAsync(DateOnly until, CancellationToken cancellationToken)
    {
        var entries = new List<DeadlineEntry>();
        foreach (var source in sources)
        {
            entries.AddRange(await source.DeadlinesAsync(until, cancellationToken));
        }

        return entries.OrderBy(e => e.Due).ThenBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private async Task ApplyAsync(RecurringTask task, TaskInput input, CancellationToken cancellationToken)
    {
        if (input.PropertyId is { } propertyId && !await db.Set<Property>().AnyAsync(o => o.Id == propertyId, cancellationToken))
        {
            throw new NotFoundException();
        }

        task.PropertyId = input.PropertyId;
        task.LeadDays = input.LeadDays;
        task.Note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim();
        task.Active = input.Active;
    }
}

/// <summary>Reminder mails with the notice of each deadline and the weekly summary.</summary>
internal sealed class DeadlineReminders(DeadlineService deadlines, ElocaroDbContext db, IMailSender mail, IOptions<ApplicationOptions> app, TimeProvider clock)
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    private DateOnly Today => GermanTime.Today(clock.GetUtcNow());

    /// <summary>Sends one mail with all deadlines whose notice has begun and for which none has been sent yet.</summary>
    /// <returns>Number of deadlines reminded.</returns>
    public async Task<int> SendRemindersAsync(CancellationToken cancellationToken = default)
    {
        var settings = await DeadlineService.SettingsOrDefaultAsync(db, cancellationToken);
        if (!settings.SendReminders)
        {
            return 0;
        }

        var today = Today;
        var entries = await deadlines.AllAsync(today.AddDays(365), cancellationToken);
        var candidates = entries.Where(e => e.Due.AddDays(-(e.LeadDays ?? settings.DefaultLeadDays)) <= today).ToList();
        if (candidates.Count == 0)
        {
            return 0;
        }

        var keys = candidates.Select(e => e.Key).ToList();
        var sent = (await db.Set<DeadlineReminder>().AsNoTracking().Where(r => keys.Contains(r.Key)).Select(r => new { r.Key, r.Due }).ToListAsync(cancellationToken))
            .Select(r => (r.Key, r.Due)).ToHashSet();
        var due = candidates.Where(e => !sent.Contains((e.Key, e.Due))).ToList();
        var recipients = await AdminRecipients.GetAsync(db, cancellationToken);
        if (due.Count == 0 || recipients.Count == 0)
        {
            return 0;
        }

        await mail.EnqueueAsync(new MailRequest(recipients, "deadline-reminder", Model(due, today), Reference: "deadline-reminder"), cancellationToken);
        foreach (var entry in due)
        {
            db.Add(new DeadlineReminder(entry.Key, entry.Due, clock.GetUtcNow()));
        }

        await db.SaveChangesAsync(cancellationToken);
        return due.Count;
    }

    /// <summary>Mondays: everything overdue and due in the next 30 days; nothing is sent when there is nothing.</summary>
    /// <returns>Number of deadlines in the summary.</returns>
    public async Task<int> SendWeeklySummaryAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        var today = Today;
        var settings = await DeadlineService.SettingsOrDefaultAsync(db, cancellationToken);
        if (!settings.SendWeeklySummary || (!force && today.DayOfWeek != DayOfWeek.Monday))
        {
            return 0;
        }

        var entries = await deadlines.AllAsync(today.AddDays(30), cancellationToken);
        var recipients = await AdminRecipients.GetAsync(db, cancellationToken);
        if (entries.Count == 0 || recipients.Count == 0)
        {
            return 0;
        }

        await mail.EnqueueAsync(new MailRequest(recipients, "deadline-summary", Model(entries, today), Reference: "deadline-summary"), cancellationToken);
        return entries.Count;
    }

    private object Model(IReadOnlyList<DeadlineEntry> entries, DateOnly today) => new
    {
        Entries = entries.Select(e =>
        {
            var days = e.Due.DayNumber - today.DayNumber;
            var when = days < 0 ? $"seit {-days} Tagen überfällig" : days == 0 ? "heute" : days == 1 ? "morgen" : $"in {days} Tagen";
            return $"{e.Due.ToString("dd.MM.yyyy", German)} ({when}): {e.Title}";
        }).ToList(),
        Link = app.Value.Link("/verwaltung/fristen").ToString(),
    };
}

[DisallowConcurrentExecution]
internal sealed class DeadlineReminderJob(DeadlineReminders reminders) : IJob
{
    public static readonly JobKey Key = new("deadline-reminders", "deadlines");

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        await reminders.SendRemindersAsync(cancellationToken);
        await reminders.SendWeeklySummaryAsync(false, cancellationToken);
    }
}

/// <summary>Dashboard: overdue deadlines and those due within a week.</summary>
[Module(AppModule.Deadlines)]
internal sealed class DeadlineTodos(IDeadlines deadlines, TimeProvider clock) : ITodoSource
{
    public async Task<IReadOnlyList<TodoItem>> TodosAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        var today = GermanTime.Today(clock.GetUtcNow());
        var entries = await deadlines.ListAsync(user, today.AddDays(7), cancellationToken);
        return entries.Where(e => e.Origin == DeadlineOrigin.Task).Select(e => new TodoItem(
            e.Area, e.Due < today ? $"Überfällig: {e.Title}" : e.Title, e.Link, e.Due < today ? Urgency.Urgent : Urgency.Soon, e.Details, e.Due)).ToList();
    }
}
