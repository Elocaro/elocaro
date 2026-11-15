using System.Globalization;
using System.Security.Claims;
using System.Text;
using Elocaro.Application.Accounts;
using Elocaro.Application.Common;
using Elocaro.Application.Portfolio;
using Elocaro.Domain.Common;
using Elocaro.Domain.Portfolio;
using Elocaro.Web.Display;

namespace Elocaro.Web.Finance;

internal static class RentPotentialEndpoint
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>Rent potential of a property as CSV (semicolon, German numbers, UTF-8 with BOM for Excel).</summary>
    public static IEndpointRouteBuilder MapRentPotentialEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/verwaltung/objekte/{propertyId:guid}/mieterhoehungspotenzial.csv", async (Guid propertyId, IRentPotential potential, TimeProvider clock, ClaimsPrincipal user, HttpContext context) =>
        {
            var result = await potential.GetAsync(user, propertyId, context.RequestAborted);
            if (result is null)
            {
                return Results.NotFound();
            }

            context.Response.Headers.CacheControl = "no-store";
            return Results.File(Csv(result.Rows), "text/csv; charset=utf-8", $"mieterhoehungspotenzial-{GermanTime.Today(clock.GetUtcNow()):yyyy-MM-dd}.csv");
        }).RequireAuthorization(Policies.Finance);
        return endpoints;
    }

    internal static byte[] Csv(IEnumerable<RentPotentialRow> rows)
    {
        var text = new StringBuilder();
        text.Append("Mietobjekt;Kaltmiete;Wohnfläche;Miete je m²;Vergleichsmiete;Kappungsgrenze;Zulässig bis;Potenzial;Begrenzt durch;Frühester Zugang;Wirksam ab;Rechenweg\r\n");
        foreach (var r in rows)
        {
            text.AppendJoin(';',
                OpenItemsEndpoint.Field(r.Unit),
                Number(r.CurrentRent),
                Number(r.LivingArea),
                Number(r.CurrentPerSquareMetre),
                Number(r.ComparativeRent),
                Number(r.CapLimit),
                Number(r.MaxRent),
                Number(r.Potential),
                Limit(r.Limit),
                Date(r.EarliestDeclaration),
                Date(r.EffectiveFrom),
                OpenItemsEndpoint.Field(r.Calculation)).Append("\r\n");
        }

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text.ToString())];
    }

    internal static string Limit(RentPotentialLimit limit) => limit switch
    {
        RentPotentialLimit.ComparativeRent => "Vergleichsmiete",
        RentPotentialLimit.CapLimit => "Kappungsgrenze",
        RentPotentialLimit.NotAssessable => "nicht bewertbar",
        _ => "kein Potenzial",
    };

    private static string Number(decimal? value) => value?.ToString("0.00", German) ?? "";

    private static string Date(DateOnly? value) => value?.ToString("dd.MM.yyyy", German) ?? "";
}
