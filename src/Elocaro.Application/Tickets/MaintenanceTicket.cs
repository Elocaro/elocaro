using Elocaro.Application.Deadlines;
using Elocaro.Domain.Deadlines;
using Elocaro.Domain.Tickets;

namespace Elocaro.Application.Tickets;

/// <summary>Pre-filled ticket for a maintenance task (the administration creates it by hand and then assigns it).</summary>
public static class MaintenanceTicket
{
    public static TicketCategory CategoryFor(TaskKind kind) => kind switch
    {
        TaskKind.HeatingService or TaskKind.ChimneySweep => TicketCategory.Heating,
        TaskKind.DrinkingWater => TicketCategory.Water,
        TaskKind.SmokeDetectors => TicketCategory.Electrical,
        TaskKind.Gutters or TaskKind.Playground => TicketCategory.CommonAreas,
        _ => TicketCategory.Other,
    };

    public static void Prefill(AdminTicketInput input, TaskListItem task)
    {
        input.Title = task.Title.Length <= 200 ? task.Title : task.Title[..200];
        input.Category = CategoryFor(task.Kind);
        input.PropertyId = task.PropertyId;
        input.Description = $"Wartung „{task.Title}“, fällig am {task.DueOn:dd.MM.yyyy}.";
    }
}
