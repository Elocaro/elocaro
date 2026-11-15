using System.Globalization;
using Elocaro.Application.Common;
using Elocaro.Application.Payments;
using Microsoft.AspNetCore.Antiforgery;

namespace Elocaro.Web.Finance;

internal static class DirectDebitEndpoint
{
    public const string Path = "/verwaltung/finanzen/lastschriften/datei";
    private const string ListPath = "/verwaltung/finanzen/lastschriften";

    /// <summary>SEPA direct debit file for the chosen rents (POST with antiforgery token, so no link can trigger it). Errors lead back to the list.</summary>
    public static IEndpointRouteBuilder MapDirectDebitEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(Path, async (HttpContext context, IDirectDebits debits, IAntiforgery antiforgery) =>
        {
            if (!await antiforgery.IsRequestValidAsync(context))
            {
                return Results.StatusCode(StatusCodes.Status400BadRequest);
            }

            var form = await context.Request.ReadFormAsync(context.RequestAborted);
            if (!DateOnly.TryParseExact(form["einzugstag"].ToString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                return Results.Redirect(ListPath + "?fehler=" + Uri.EscapeDataString("Bitte den Einzugstag angeben."));
            }

            var result = await debits.CreateFileAsync([.. form["auswahl"].Select(v => v ?? "")], date, context.RequestAborted);
            if (!result.Succeeded)
            {
                return Results.Redirect(ListPath + "?fehler=" + Uri.EscapeDataString(string.Join(" ", result.Errors)));
            }

            context.Response.Headers.CacheControl = "no-store";
            return Results.File(result.Content!, "application/xml", result.FileName);
        }).RequireAuthorization(Policies.Finance);
        return endpoints;
    }
}
