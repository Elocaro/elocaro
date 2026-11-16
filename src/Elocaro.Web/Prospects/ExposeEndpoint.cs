using System.Globalization;
using Elocaro.Application.Common;
using Elocaro.Application.Prospects;
using Microsoft.AspNetCore.Antiforgery;

namespace Elocaro.Web.Prospects;

internal static class ExposeEndpoint
{
    public const string Path = "/verwaltung/interessenten/expose/datei";
    public const string PagePath = "/verwaltung/interessenten/expose";

    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>
    /// Exposé as PDF (POST with antiforgery token). "Hinweise prüfen" leads back to the page with the entries in the query (POST-redirect-GET), so nothing sensitive is cached
    /// and the page stays a plain static page.
    /// </summary>
    public static IEndpointRouteBuilder MapExposeEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(Path, async (HttpContext context, IExposes exposes, IAntiforgery antiforgery) =>
        {
            if (!await antiforgery.IsRequestValidAsync(context))
            {
                return Results.StatusCode(StatusCodes.Status400BadRequest);
            }

            var form = await context.Request.ReadFormAsync(context.RequestAborted);
            string? Field(string name) => form[name].ToString();
            if (Field("aktion") == "pruefen")
            {
                var query = string.Join('&', ExposeForm.Names.Select(n => $"{n}={Uri.EscapeDataString(Field(n) ?? "")}"));
                return Results.Redirect($"{PagePath}?{query}");
            }

            if (!Guid.TryParse(Field("mietobjekt"), out var unit))
            {
                return Results.Redirect($"{PagePath}?fehler=" + Uri.EscapeDataString("Bitte die Einheit wählen."));
            }

            var file = await exposes.CreateAsync(unit, ExposeForm.Read(Field), context.RequestAborted);
            if (!file.Result.Succeeded)
            {
                return Results.Redirect($"{PagePath}?mietobjekt={unit}&fehler=" + Uri.EscapeDataString(string.Join(" ", file.Result.Errors)));
            }

            context.Response.Headers.CacheControl = "no-store";
            return Results.File(file.Content!, "application/pdf", file.FileName);
        }).RequireAuthorization(Policies.MasterData);
        return endpoints;
    }

    /// <summary>The entries of the exposé form, read from the form or from the query.</summary>
    internal static class ExposeForm
    {
        public static readonly string[] Names = ["mietobjekt", "miete", "nk", "hk", "kaution", "frei", "vormiete", "text"];

        public static ExposeInput Read(Func<string, string?> field) => new()
        {
            Rent = Money(field("miete")),
            OperatingCosts = Money(field("nk")),
            HeatingCosts = Money(field("hk")),
            Deposit = Money(field("kaution")),
            PreviousRent = Money(field("vormiete")),
            AvailableFrom = DateOnly.TryParseExact(field("frei"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null,
            Description = string.IsNullOrWhiteSpace(field("text")) ? null : field("text")!.Trim(),
        };

        /// <summary>"850", "850,50" and "850.50" (a point as decimal separator is accepted; thousands separators are not).</summary>
        public static decimal? Money(string? text) =>
            decimal.TryParse(text?.Trim().Replace(" ", "", StringComparison.Ordinal), NumberStyles.AllowDecimalPoint, German, out var value) ? value
            : decimal.TryParse(text?.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value) ? value : null;
    }
}
