using Microsoft.AspNetCore.Components;

namespace MietHahn.Web.Konto;

/// <summary>
/// Weiterleitungen aus statisch gerenderten Seiten, mit Schutz vor offenen Weiterleitungen (nur lokale Ziele)
/// und optionaler Statusmeldung für die Zielseite (kurzlebiges Cookie).
/// </summary>
internal sealed class Weiterleitung(NavigationManager navigation)
{
    public const string StatusCookie = "miethahn.status";

    private static readonly CookieBuilder StatusCookieVorlage = new()
    {
        SameSite = SameSiteMode.Strict,
        HttpOnly = true,
        IsEssential = true,
        MaxAge = TimeSpan.FromSeconds(10),
    };

    /// <summary>Nur relative, lokale Ziele; alles andere führt zur Startseite.</summary>
    public static string LokalesZiel(string? ziel) =>
        string.IsNullOrWhiteSpace(ziel)
        || !Uri.IsWellFormedUriString(ziel, UriKind.Relative)
        || ziel.StartsWith("//", StringComparison.Ordinal)
        || ziel.StartsWith("/\\", StringComparison.Ordinal)
        || ziel.Contains("://", StringComparison.Ordinal)
            ? "/"
            : ziel.StartsWith('/') ? ziel : "/" + ziel;

    public void Zu(string? ziel) => navigation.NavigateTo(LokalesZiel(ziel), forceLoad: false);

    public void Zu(string ziel, IReadOnlyDictionary<string, object?> parameter) =>
        navigation.NavigateTo(navigation.GetUriWithQueryParameters(navigation.ToAbsoluteUri(LokalesZiel(ziel)).GetLeftPart(UriPartial.Path), parameter));

    public void ZuMitMeldung(string ziel, string meldung, HttpContext kontext)
    {
        ArgumentNullException.ThrowIfNull(kontext);
        kontext.Response.Cookies.Append(StatusCookie, Uri.EscapeDataString(meldung), StatusCookieVorlage.Build(kontext));
        Zu(ziel);
    }

    /// <summary>Liest (und löscht) eine Statusmeldung, die von der vorherigen Seite gesetzt wurde.</summary>
    public static string? MeldungLesen(HttpContext kontext)
    {
        ArgumentNullException.ThrowIfNull(kontext);
        if (!kontext.Request.Cookies.TryGetValue(StatusCookie, out var wert))
        {
            return null;
        }

        kontext.Response.Cookies.Delete(StatusCookie);
        return Uri.UnescapeDataString(wert);
    }
}
