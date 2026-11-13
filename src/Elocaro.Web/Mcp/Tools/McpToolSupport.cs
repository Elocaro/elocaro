using Elocaro.Application.Accounts;
using Microsoft.Extensions.Options;

namespace Elocaro.Web.Mcp.Tools;

/// <summary>A cut-down list: the tools never hand out everything at once.</summary>
public sealed record McpList<T>(int Total, int Shown, IReadOnlyList<T> Items, string? Note);

internal static class McpToolSupport
{
    public static McpList<T> Limit<T>(this IReadOnlyCollection<T> source, IOptions<McpOptions> options)
    {
        ArgumentNullException.ThrowIfNull(source);
        var max = Math.Clamp(options.Value.MaxResults, 1, 200);
        var items = source.Take(max).ToList();
        return new McpList<T>(source.Count, items.Count, items, source.Count > max ? $"Es werden nur die ersten {max} Einträge gezeigt. Bitte die Suche eingrenzen." : null);
    }
}

/// <summary>The result of a changing tool call: what happened, the id of the record and where to take it back.</summary>
public sealed record McpChange(bool Succeeded, Guid? Id, IReadOnlyList<string> Errors, string? Note)
{
    internal const string UndoNote = "Rückgängig machen: Elocaro → Einstellungen → System → Aktivität.";

    public static McpChange From(Elocaro.Application.Common.Result result, Guid? id = null) =>
        new(result.Succeeded, result.Id ?? id, result.Errors, result.Succeeded ? UndoNote : null);

    public static McpChange Failed(string error) => new(false, null, [error], null);
}
