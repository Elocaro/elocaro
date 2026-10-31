using Elocaro.Application.Accounts;
using Elocaro.Application.Emergency;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Net.Http.Headers;

namespace Elocaro.Web.Operations;

internal static class EmergencyFolderEndpoint
{
    public const string PagePath = "/verwaltung/system/notfallmappe";
    public const string Path = PagePath + "/erzeugen";

    /// <summary>Texts for <c>?fehler=key</c> on the page: fixed keys only, nothing from the request is echoed.</summary>
    public static IReadOnlyDictionary<string, string> Errors { get; } = new Dictionary<string, string>
    {
        ["passwort-kurz"] = "Das Passwort braucht mindestens 12 Zeichen. Die Hinweise sind trotzdem gespeichert.",
        ["passwort-ungleich"] = "Die beiden Passwörter stimmen nicht überein. Die Hinweise sind trotzdem gespeichert.",
        ["zu-gross"] = "Die Mappe wäre zu groß (Grenze 40 MB). Bitte ohne Mietvertragsdokumente erzeugen.",
        ["hinweise-zu-lang"] = "Ein Hinweistext ist zu lang (höchstens 4.000 Zeichen). Nichts wurde gespeichert.",
    };

    /// <summary>
    /// Saves the notes and, on request, creates the encrypted folder as a download. POST with antiforgery token, so no link can
    /// trigger it. The password only travels in the request body and is never written to a URL, log or audit entry.
    /// </summary>
    public static IEndpointRouteBuilder MapEmergencyFolderEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(Path, async (HttpContext context, IEmergencyFolder folder, IAntiforgery antiforgery) =>
        {
            if (!await antiforgery.IsRequestValidAsync(context))
            {
                return Results.StatusCode(StatusCodes.Status400BadRequest);
            }

            var form = await context.Request.ReadFormAsync(context.RequestAborted);
            var saved = await folder.SaveNotesAsync(
                new EmergencyFolderNotes
                {
                    ImportantContacts = Text(form["ImportantContacts"]),
                    DocumentLocation = Text(form["DocumentLocation"]),
                    FurtherNotes = Text(form["FurtherNotes"]),
                },
                context.RequestAborted);
            if (!saved.Succeeded)
            {
                return Results.Redirect($"{PagePath}?fehler={Key(saved.Problem)}");
            }

            if (form["Action"] != "create")
            {
                return Results.Redirect($"{PagePath}?meldung=gespeichert");
            }

            var result = await folder.CreateAsync(
                new EmergencyFolderRequest(form["Password"].ToString(), form["PasswordConfirmation"].ToString(), form.ContainsKey("IncludeLeaseDocuments")),
                context.User,
                context.RequestAborted);
            if (!result.Succeeded)
            {
                return Results.Redirect($"{PagePath}?fehler={Key(result.Problem)}");
            }

            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileNameStar = result.FileName }.ToString();
            context.Response.Headers.CacheControl = "no-store";
            context.Response.ContentLength = result.Content!.Length;
            await context.Response.Body.WriteAsync(result.Content, context.RequestAborted);
            return Results.Empty;
        }).RequireAuthorization(Policies.System);
        return endpoints;
    }

    /// <summary>Browsers send line breaks as CRLF; store LF so the length limit counts like on the page.</summary>
    private static string? Text(string? value) => value?.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string Key(EmergencyFolderProblem problem) => problem switch
    {
        EmergencyFolderProblem.PasswordTooShort => "passwort-kurz",
        EmergencyFolderProblem.PasswordsDiffer => "passwort-ungleich",
        EmergencyFolderProblem.TooLarge => "zu-gross",
        _ => "hinweise-zu-lang",
    };
}
