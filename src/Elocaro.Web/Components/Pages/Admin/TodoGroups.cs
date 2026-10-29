using Elocaro.Application.Todos;

namespace Elocaro.Web.Components.Pages.Admin;

/// <summary>One line of an area on the start page: a single task, or several tasks of the same kind in a group.</summary>
internal sealed record TodoEntry(TodoItem First, string Heading, IReadOnlyList<TodoItem> Items)
{
    public bool IsGroup => Items.Count > 1;
}

/// <summary>
/// Combines reminders that repeat for many objects ("Eichfrist abgelaufen: Zähler 1" … "Zähler 20", same date and text)
/// into one entry, so that twenty identical cards do not push everything else off the start page.
/// </summary>
internal static class TodoGroups
{
    /// <summary>From this many tasks of the same kind on, they are shown as a group.</summary>
    public const int GroupFrom = 3;

    /// <summary>Entries in the order of the first task of each kind.</summary>
    public static List<TodoEntry> Build(IEnumerable<TodoItem> ordered)
    {
        var entries = new List<TodoEntry>();
        foreach (var kind in ordered.GroupBy(t => (Heading: HeadingOf(t.Title), t.Details, t.Due, t.Urgency)))
        {
            var items = kind.ToList();
            if (kind.Key.Heading is not null && items.Count >= GroupFrom)
            {
                entries.Add(new TodoEntry(items[0], kind.Key.Heading, items));
            }
            else
            {
                entries.AddRange(items.Select(item => new TodoEntry(item, "", [item])));
            }
        }

        return entries;
    }

    /// <summary>The part before the colon ("Eichfrist abgelaufen"), or null when the title has no such lead-in.</summary>
    public static string? HeadingOf(string title)
    {
        var colon = title.IndexOf(": ", StringComparison.Ordinal);
        return colon > 0 ? title[..colon] : null;
    }

    /// <summary>The title without the lead-in of its group ("Zähler 1").</summary>
    public static string Rest(TodoItem item, string heading) => item.Title[(heading.Length + 2)..];
}
