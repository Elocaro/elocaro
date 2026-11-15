using System.Globalization;
using System.Security.Claims;
using System.Text;
using Elocaro.Application.Common;
using Elocaro.Application.Portfolio;
using Elocaro.Domain.Common;
using Elocaro.Domain.Portfolio;

namespace Elocaro.Web.Finance;

internal static class KeyFiguresEndpoint
{
    /// <summary>Number of years in the chart and the export, counted back from the current year.</summary>
    internal const int Years = 5;

    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>Key figures of a property for the last years as CSV (semicolon, German numbers, UTF-8 with BOM for Excel).</summary>
    public static IEndpointRouteBuilder MapKeyFiguresEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/verwaltung/objekte/{propertyId:guid}/kennzahlen.csv", async (Guid propertyId, IPropertyKeyFigures figures, TimeProvider clock, ClaimsPrincipal user, HttpContext context) =>
        {
            var thisYear = GermanTime.Today(clock.GetUtcNow()).Year;
            var rows = new List<PropertyKeyFigures>();
            foreach (var year in Enumerable.Range(thisYear - Years + 1, Years))
            {
                if (await figures.GetAsync(user, propertyId, year, context.RequestAborted) is not { } row)
                {
                    return Results.NotFound();
                }

                rows.Add(row);
            }

            context.Response.Headers.CacheControl = "no-store";
            return Results.File(Csv(rows), "text/csv; charset=utf-8", $"kennzahlen-{thisYear}.csv");
        }).RequireAuthorization(Policies.Finance);
        return endpoints;
    }

    internal static byte[] Csv(IEnumerable<PropertyKeyFigures> rows)
    {
        var text = new StringBuilder();
        text.Append("Jahr;Nettomietrendite (%);Cashflow vor Tilgung;Cashflow;Leerstandsquote (%);Rückstandsquote (%)\r\n");
        foreach (var r in rows)
        {
            text.AppendJoin(';', r.Year.ToString(German), Number(r.NetYield), Number(r.CashflowBeforeRepayment), Number(r.Cashflow), Number(r.VacancyPercent), Number(r.ArrearsPercent)).Append("\r\n");
        }

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text.ToString())];
    }

    private static string Number(decimal? value) => value?.ToString("0.00", German) ?? "";
}
