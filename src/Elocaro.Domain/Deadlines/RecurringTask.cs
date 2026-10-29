using Elocaro.Domain.Common;

namespace Elocaro.Domain.Deadlines;

public enum TaskKind
{
    SmokeDetectors = 0,
    HeatingService = 1,
    ChimneySweep = 2,
    DrinkingWater = 3,
    Gutters = 4,
    Playground = 5,
    Insurance = 6,
    Other = 99,
}

/// <summary>
/// A maintenance or recurring duty entered by hand (smoke detectors, heating service, chimney sweep ...). Completing it records
/// the proof and moves the due date by the interval; a one-off task (interval 0) is closed.
/// </summary>
public sealed class RecurringTask : Entity
{
    public const int MaxIntervalMonths = 120;

    private RecurringTask()
    {
        Title = "";
    }

    public RecurringTask(string title, TaskKind kind, DateOnly dueOn, int intervalMonths)
    {
        Title = "";
        Update(title, kind, dueOn, intervalMonths);
    }

    public string Title { get; private set; }

    public TaskKind Kind { get; private set; }

    /// <summary>Property the task belongs to; empty = applies to all.</summary>
    public Guid? PropertyId { get; set; }

    public DateOnly DueOn { get; private set; }

    /// <summary>Months between two occurrences; 0 = one-off.</summary>
    public int IntervalMonths { get; private set; }

    /// <summary>Days of notice for the reminder mail; empty = default of the settings.</summary>
    public int? LeadDays { get; set; }

    public string? Note { get; set; }

    /// <summary>A closed one-off task or a switched-off task shows up nowhere.</summary>
    public bool Active { get; set; } = true;

    public DateOnly? LastDoneOn { get; private set; }

    public void Update(string title, TaskKind kind, DateOnly dueOn, int intervalMonths)
    {
        title = title?.Trim() ?? "";
        if (title.Length == 0)
        {
            throw new RuleViolationException("Bitte eine Bezeichnung angeben.");
        }

        if (intervalMonths is < 0 or > MaxIntervalMonths)
        {
            throw new RuleViolationException($"Der Abstand muss zwischen 0 (einmalig) und {MaxIntervalMonths} Monaten liegen.");
        }

        Title = title;
        Kind = kind;
        DueOn = dueOn;
        IntervalMonths = intervalMonths;
    }

    /// <summary>Records the completion. The next date keeps the rhythm and lies after <paramref name="doneOn"/>.</summary>
    public TaskCompletion Complete(DateOnly doneOn, Guid? documentId, string? note)
    {
        if (!Active)
        {
            throw new RuleViolationException("Die Aufgabe ist nicht mehr aktiv.");
        }

        var completion = new TaskCompletion(Id, doneOn, documentId, note);
        LastDoneOn = LastDoneOn is { } last && last > doneOn ? last : doneOn;
        if (IntervalMonths == 0)
        {
            Active = false;
            return completion;
        }

        var next = DueOn;
        do
        {
            next = next.AddMonths(IntervalMonths);
        }
        while (next <= doneOn);
        DueOn = next;
        return completion;
    }
}

/// <summary>Done entry of a <see cref="RecurringTask"/>, optionally with a document as proof.</summary>
public sealed class TaskCompletion : Entity
{
    private TaskCompletion()
    {
    }

    internal TaskCompletion(Guid taskId, DateOnly doneOn, Guid? documentId, string? note)
    {
        TaskId = taskId;
        DoneOn = doneOn;
        DocumentId = documentId;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }

    public Guid TaskId { get; private set; }

    public DateOnly DoneOn { get; private set; }

    /// <summary>Document that proves the work (invoice, protocol).</summary>
    public Guid? DocumentId { get; private set; }

    public string? Note { get; private set; }
}

/// <summary>Settings of the deadline calendar (single record).</summary>
public sealed class DeadlineSettings : Entity
{
    public const int DefaultLead = 30;

    /// <summary>Notice in days before a deadline for which the reminder mail is sent.</summary>
    public int DefaultLeadDays { get; set; } = DefaultLead;

    public bool SendReminders { get; set; } = true;

    /// <summary>Mail every Monday with everything due in the next 30 days and what is overdue.</summary>
    public bool SendWeeklySummary { get; set; } = true;

    /// <summary>Secret part of the calendar link (iCal); empty = feed switched off.</summary>
    [Confidential]
    [NoExport]
    public string? CalendarToken { get; private set; }

    public void SwitchOffCalendar() => CalendarToken = null;

    public void SetCalendarToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length < 32)
        {
            throw new RuleViolationException("Das Kalender-Token ist zu kurz.");
        }

        CalendarToken = token;
    }
}

/// <summary>Remembers which reminder was sent so it is sent only once per deadline.</summary>
[NoAudit]
public sealed class DeadlineReminder : Entity
{
    private DeadlineReminder()
    {
        Key = "";
    }

    public DeadlineReminder(string key, DateOnly due, DateTimeOffset sentAt)
    {
        Key = key;
        Due = due;
        SentAt = sentAt;
    }

    /// <summary>Stable identity of the deadline, e.g. <c>statement:{property}:{year}</c>.</summary>
    public string Key { get; private set; }

    public DateOnly Due { get; private set; }

    public DateTimeOffset SentAt { get; private set; }
}

/// <summary>A day of the calendar feed.</summary>
public sealed record CalendarEvent(string Uid, DateOnly Date, string Summary, string? Description, string? Url);

/// <summary>Writes events as iCalendar (RFC 5545): all-day events, UTF-8, lines folded to 75 octets.</summary>
public static class ICalendarWriter
{
    public static string Write(IEnumerable<CalendarEvent> events, DateTimeOffset generatedAt, string calendarName)
    {
        ArgumentNullException.ThrowIfNull(events);
        var lines = new List<string>
        {
            "BEGIN:VCALENDAR",
            "VERSION:2.0",
            "PRODID:-//Elocaro//Fristen//DE",
            "CALSCALE:GREGORIAN",
            "METHOD:PUBLISH",
            "X-WR-CALNAME:" + Escape(calendarName),
        };
        var stamp = generatedAt.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", System.Globalization.CultureInfo.InvariantCulture);
        foreach (var e in events)
        {
            lines.Add("BEGIN:VEVENT");
            lines.Add("UID:" + Escape(e.Uid) + "@elocaro");
            lines.Add("DTSTAMP:" + stamp);
            lines.Add("DTSTART;VALUE=DATE:" + e.Date.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture));
            lines.Add("DTEND;VALUE=DATE:" + e.Date.AddDays(1).ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture));
            lines.Add("SUMMARY:" + Escape(e.Summary));
            if (!string.IsNullOrWhiteSpace(e.Description))
            {
                lines.Add("DESCRIPTION:" + Escape(e.Description));
            }

            if (!string.IsNullOrWhiteSpace(e.Url))
            {
                lines.Add("URL:" + e.Url);
            }

            lines.Add("TRANSP:TRANSPARENT");
            lines.Add("END:VEVENT");
        }

        lines.Add("END:VCALENDAR");
        return string.Concat(lines.SelectMany(Fold).Select(l => l + "\r\n"));
    }

    private static string Escape(string text) =>
        text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace(";", "\\;", StringComparison.Ordinal).Replace(",", "\\,", StringComparison.Ordinal)
            .Replace("\r\n", "\\n", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal).Replace("\r", "\\n", StringComparison.Ordinal);

    /// <summary>Continuation lines start with a blank; folded by octets, never inside a character.</summary>
    private static IEnumerable<string> Fold(string line)
    {
        var current = new System.Text.StringBuilder();
        var octets = 0;
        foreach (var rune in line.EnumerateRunes())
        {
            var size = rune.Utf8SequenceLength;
            if (octets + size > 75)
            {
                yield return current.ToString();
                current.Clear().Append(' ');
                octets = 1;
            }

            current.Append(rune.ToString());
            octets += size;
        }

        yield return current.ToString();
    }
}
