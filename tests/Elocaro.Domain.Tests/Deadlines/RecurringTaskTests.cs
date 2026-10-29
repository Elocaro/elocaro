using Elocaro.Domain.Common;
using Elocaro.Domain.Deadlines;

namespace Elocaro.Domain.Tests.Deadlines;

public class RecurringTaskTests
{
    [Fact]
    public void Completion_moves_the_due_date_by_the_interval_and_keeps_the_rhythm()
    {
        var task = new RecurringTask("Rauchmelder prüfen", TaskKind.SmokeDetectors, new DateOnly(2026, 10, 1), 12);

        var completion = task.Complete(new DateOnly(2026, 10, 20), null, "  alle ok ");

        Assert.Equal(new DateOnly(2027, 10, 1), task.DueOn);
        Assert.Equal(new DateOnly(2026, 10, 20), task.LastDoneOn);
        Assert.Equal("alle ok", completion.Note);
        Assert.True(task.Active);
    }

    [Fact]
    public void Missed_occurrences_are_skipped_so_the_next_date_lies_after_the_completion()
    {
        var task = new RecurringTask("Heizung warten", TaskKind.HeatingService, new DateOnly(2024, 9, 1), 12);

        task.Complete(new DateOnly(2026, 10, 20), null, null);

        Assert.Equal(new DateOnly(2027, 9, 1), task.DueOn);
    }

    [Fact]
    public void One_off_task_is_closed_by_completion()
    {
        var task = new RecurringTask("Dach prüfen", TaskKind.Other, new DateOnly(2026, 10, 1), 0);

        task.Complete(new DateOnly(2026, 10, 2), Guid.NewGuid(), null);

        Assert.False(task.Active);
        Assert.Throws<RuleViolationException>(() => task.Complete(new DateOnly(2026, 10, 3), null, null));
    }

    [Theory]
    [InlineData("", 12)]
    [InlineData("  ", 12)]
    [InlineData("Schornsteinfeger", -1)]
    [InlineData("Schornsteinfeger", 121)]
    public void Invalid_data_is_rejected(string title, int interval) =>
        Assert.Throws<RuleViolationException>(() => new RecurringTask(title, TaskKind.ChimneySweep, new DateOnly(2026, 10, 1), interval));
}

public class ICalendarWriterTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Events_are_written_as_all_day_entries_with_crlf()
    {
        var text = ICalendarWriter.Write([new CalendarEvent("a:1", new DateOnly(2026, 12, 31), "Abrechnung; Frist, wichtig", "Zeile 1\nZeile 2", "https://x.de/f")], Now, "Elocaro Fristen");

        Assert.StartsWith("BEGIN:VCALENDAR\r\n", text, StringComparison.Ordinal);
        Assert.EndsWith("END:VCALENDAR\r\n", text, StringComparison.Ordinal);
        Assert.Contains("DTSTART;VALUE=DATE:20261231\r\n", text, StringComparison.Ordinal);
        Assert.Contains("DTEND;VALUE=DATE:20270101\r\n", text, StringComparison.Ordinal);
        Assert.Contains("DTSTAMP:20261021T080000Z\r\n", text, StringComparison.Ordinal);
        Assert.Contains("SUMMARY:Abrechnung\\; Frist\\, wichtig\r\n", text, StringComparison.Ordinal);
        Assert.Contains("DESCRIPTION:Zeile 1\\nZeile 2\r\n", text, StringComparison.Ordinal);
        Assert.Contains("UID:a:1@elocaro\r\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Long_lines_are_folded_to_75_octets_without_splitting_characters()
    {
        var text = ICalendarWriter.Write([new CalendarEvent("u", new DateOnly(2026, 1, 1), new string('ä', 80), null, null)], Now, "x");

        foreach (var line in text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            Assert.True(System.Text.Encoding.UTF8.GetByteCount(line) <= 75, line);
        }

        var unfolded = text.Replace("\r\n ", "", StringComparison.Ordinal);
        Assert.Contains("SUMMARY:" + new string('ä', 80), unfolded, StringComparison.Ordinal);
    }
}
