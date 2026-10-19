using Microsoft.AspNetCore.Components;

namespace MietHahn.Web.Account;

/// <summary>
/// Redirects from statically rendered pages, protected against open redirects (local targets only)
/// and an optional status message for the target page (short-lived cookie).
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

    /// <summary>Only relative, local targets; anything else leads to the start page.</summary>
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

    /// <summary>Reads (and deletes) a status message set by the previous page.</summary>
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
