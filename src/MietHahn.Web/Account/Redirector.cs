using Microsoft.AspNetCore.Components;

namespace MietHahn.Web.Account;

/// <summary>
/// Weiterleitungen aus statisch gerenderten Seiten, mit Schutz vor offenen Weiterleitungen (nur lokale Ziele)
/// und optionaler Statusmeldung für die Zielseite (kurzlebiges Cookie).
/// </summary>
internal sealed class Redirector(NavigationManager navigation)
{
    public const string StatusCookie = "miethahn.status";

    private static readonly CookieBuilder StatusCookieTemplate = new()
    {
        SameSite = SameSiteMode.Strict,
        HttpOnly = true,
        IsEssential = true,
        MaxAge = TimeSpan.FromSeconds(10),
    };

    /// <summary>Nur relative, lokale Ziele; alles andere führt zur Startseite.</summary>
    public static string LocalTarget(string? destination) =>
        string.IsNullOrWhiteSpace(destination)
        || !Uri.IsWellFormedUriString(destination, UriKind.Relative)
        || destination.StartsWith("//", StringComparison.Ordinal)
        || destination.StartsWith("/\\", StringComparison.Ordinal)
        || destination.Contains("://", StringComparison.Ordinal)
            ? "/"
            : destination.StartsWith('/') ? destination : "/" + destination;

    public void To(string? destination) => navigation.NavigateTo(LocalTarget(destination), forceLoad: false);

    public void To(string destination, IReadOnlyDictionary<string, object?> parameter) =>
        navigation.NavigateTo(navigation.GetUriWithQueryParameters(navigation.ToAbsoluteUri(LocalTarget(destination)).GetLeftPart(UriPartial.Path), parameter));

    public void RedirectWithMessage(string destination, string message, HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Response.Cookies.Append(StatusCookie, Uri.EscapeDataString(message), StatusCookieTemplate.Build(context));
        To(destination);
    }

    /// <summary>Liest (und löscht) eine Statusmeldung, die von der vorherigen Seite gesetzt wurde.</summary>
    public static string? ReadMessage(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.Request.Cookies.TryGetValue(StatusCookie, out var value))
        {
            return null;
        }

        context.Response.Cookies.Delete(StatusCookie);
        return Uri.UnescapeDataString(value);
    }
}
