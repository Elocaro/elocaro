using System.Globalization;
using Elocaro.Application.Common;
using Elocaro.Application.Payments;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Net.Http.Headers;

namespace Elocaro.Web.Finance;

internal static class PaymentOrderEndpoint
{
    public const string Path = "/verwaltung/finanzen/ueberweisungen/datei";
    private const string ListPath = "/verwaltung/finanzen/ueberweisungen";

    /// <summary>SEPA transfer file for the chosen payments (POST with antiforgery token, so no link can trigger it). Errors lead back to the list.</summary>
    public static IEndpointRouteBuilder MapPaymentOrderEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(Path, async (HttpContext context, IPaymentOrders orders, IAntiforgery antiforgery) =>
        {
            if (!await antiforgery.IsRequestValidAsync(context))
            {
                return Results.StatusCode(StatusCodes.Status400BadRequest);
            }

            var form = await context.Request.ReadFormAsync(context.RequestAborted);
            if (!DateOnly.TryParseExact(form["ausfuehrung"].ToString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                return Results.Redirect(ListPath + "?fehler=" + Uri.EscapeDataString("Bitte den Ausführungstag angeben."));
            }

            var result = await orders.CreateFileAsync([.. form["auswahl"].Select(v => v ?? "")], date, context.RequestAborted);
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
