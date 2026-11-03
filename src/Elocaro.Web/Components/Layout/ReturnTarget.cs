namespace Elocaro.Web.Components.Layout;

/// <summary>
/// The page a user came from when a page was opened out of a process ("Beheben" in the statement wizard, a step of the tenant change).
/// The target travels in the query (<c>?zurueck=</c>); only paths inside the administration are accepted, so the query cannot lead elsewhere.
/// </summary>
public sealed record ReturnTarget(string Href, string Text)
{
    public const string QueryName = "zurueck";

    private const int MaxLength = 300;

    /// <summary>The target for a query value, or <c>null</c> if it is missing or not a path inside the administration.</summary>
    public static ReturnTarget? From(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)
            || path.Length > MaxLength
            || !path.StartsWith("/verwaltung/", StringComparison.Ordinal)
            || path.Contains("//", StringComparison.Ordinal)
            || path.Any(c => c < ' ' || c is '\\' or ':'))
        {
            return null;
        }

        var text = path.Contains("/abrechnung", StringComparison.Ordinal) ? "Zurück zur Abrechnung"
            : path.Contains("/auszug", StringComparison.Ordinal) ? "Zurück zum Mieterwechsel"
            : "Zurück";
        return new ReturnTarget(path, text);
    }

    /// <summary>Appends the return target to a link inside the administration.</summary>
    public static string AppendTo(string href, string back) =>
        $"{href}{(href.Contains('?', StringComparison.Ordinal) ? '&' : '?')}{QueryName}={Uri.EscapeDataString(back)}";
}
