using System.ComponentModel;
using Elocaro.Application.Accounts;
using Elocaro.Application.Deadlines;
using Elocaro.Application.Todos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace Elocaro.Web.Mcp.Tools;

/// <summary>
/// To-dos and deadlines. The sources and the deadlines filter by the rights of the principal themselves, so a token with the
/// scope read:masterdata only sees the entries of that area.
/// </summary>
[McpServerToolType]
[Authorize(Policy = Policies.Admin)]
internal sealed class OverviewTools(
    IEnumerable<ITodoSource> sources,
    IDeadlines deadlines,
    IHttpContextAccessor http,
    TimeProvider clock,
    IOptions<McpOptions> options)
{
    [McpServerTool(Name = "list_todos", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Listet die offenen Aufgaben und Hinweise des Dashboards, dringende zuerst.")]
    public async Task<McpList<TodoItem>> ListTodosAsync(CancellationToken cancellationToken)
    {
        var user = http.HttpContext!.User;
        var all = new List<TodoItem>();
        foreach (var source in sources)
        {
            all.AddRange(await source.TodosAsync(user, cancellationToken));
        }

        return all.OrderByDescending(t => t.Urgency).ThenBy(t => t.Due ?? DateOnly.MaxValue).ToList().Limit(options);
    }

    [McpServerTool(Name = "list_deadlines", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Listet Fristen und Termine (Abrechnungsfristen, Dokumentenablauf, Aufgaben) bis zu einem Datum.")]
    public async Task<McpList<DeadlineEntry>> ListDeadlinesAsync(
        [Description("Bis wann (Datum, yyyy-MM-dd); Standard: die nächsten 90 Tage")] DateOnly? until = null,
        CancellationToken cancellationToken = default) =>
        (await deadlines.ListAsync(http.HttpContext!.User, until ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime).AddDays(90), cancellationToken)).Limit(options);
}
