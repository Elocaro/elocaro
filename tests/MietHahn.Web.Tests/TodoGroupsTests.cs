using MietHahn.Application.Todos;
using MietHahn.Web.Components.Pages.Admin;

namespace MietHahn.Web.Tests;

public class TodoGroupsTests
{
    private static TodoItem Calibration(string meter, DateOnly? due = null) =>
        new("Zähler", $"Eichfrist abgelaufen: {meter}", $"/verwaltung/zaehler/{meter}", Urgency.Urgent, "Nicht geeichte Zähler dürfen nicht verwendet werden.", due ?? new DateOnly(2022, 12, 31));

    [Fact]
    public void Three_or_more_tasks_of_the_same_kind_become_one_group_with_all_links()
    {
        var entries = TodoGroups.Build([Calibration("Heizung 1"), Calibration("Heizung 2"), Calibration("Gas")]);

        var group = Assert.Single(entries);
        Assert.True(group.IsGroup);
        Assert.Equal("Eichfrist abgelaufen", group.Heading);
        Assert.Equal(["Heizung 1", "Heizung 2", "Gas"], group.Items.Select(i => TodoGroups.Rest(i, group.Heading)));
    }

    [Fact]
    public void Two_tasks_stay_single_entries()
    {
        var entries = TodoGroups.Build([Calibration("Heizung 1"), Calibration("Heizung 2")]);

        Assert.Equal(2, entries.Count);
        Assert.All(entries, e => Assert.False(e.IsGroup));
    }

    [Fact]
    public void Tasks_with_a_different_date_or_without_a_lead_in_are_not_combined()
    {
        List<TodoItem> items =
        [
            Calibration("A", new DateOnly(2022, 12, 31)),
            Calibration("B", new DateOnly(2023, 12, 31)),
            Calibration("C", new DateOnly(2024, 12, 31)),
            new("Zähler", "Zählerstand ablesen", "/a", Urgency.Soon),
            new("Zähler", "Zählerstand ablesen", "/b", Urgency.Soon),
            new("Zähler", "Zählerstand ablesen", "/c", Urgency.Soon),
        ];

        var entries = TodoGroups.Build(items);

        Assert.Equal(6, entries.Count);
        Assert.All(entries, e => Assert.False(e.IsGroup));
    }

    [Fact]
    public void The_order_follows_the_first_task_of_each_kind()
    {
        TodoItem other = new("Zähler", "Wartung fällig: Gas", "/w", Urgency.Soon);

        var entries = TodoGroups.Build([Calibration("A"), other, Calibration("B"), Calibration("C")]);

        Assert.Equal(["Eichfrist abgelaufen", ""], entries.Select(e => e.Heading));
        Assert.Equal(3, entries[0].Items.Count);
    }
}
