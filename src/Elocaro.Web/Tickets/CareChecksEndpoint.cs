using System.Globalization;
using System.Text;
using Elocaro.Application.Accounts;
using Elocaro.Application.Tickets;
using Elocaro.Domain.Common;
using Elocaro.Web.Display;
using Elocaro.Web.Finance;

namespace Elocaro.Web.Tickets;

internal static class CareChecksEndpoint
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>Proof of the caretaker duties of a year as CSV (semicolon, UTF-8 with BOM for Excel) and PDF.</summary>
    public static IEndpointRouteBuilder MapCareChecksEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/verwaltung/tickets/routinen.csv", async (ICareRoutines routines, TimeProvider clock, HttpContext context, int? jahr) =>
        {
            var year = jahr ?? GermanTime.Today(clock.GetUtcNow()).Year;
            if (year is < 2000 or > 2100)
            {
                return Results.BadRequest();
            }

            var rows = new List<(RoutineRow Routine, CheckRow Check)>();
            foreach (var routine in await routines.ListAsync(context.User, true, context.RequestAborted))
            {
                rows.AddRange((await routines.ChecksAsync(context.User, routine.Id, year, context.RequestAborted)).Select(c => (routine, c)));
            }

            context.Response.Headers.CacheControl = "no-store";
            return Results.File(Csv(rows.OrderBy(r => r.Routine.Property, StringComparer.CurrentCulture).ThenBy(r => r.Routine.Title, StringComparer.CurrentCulture).ThenBy(r => r.Check.DoneOn)),
                "text/csv; charset=utf-8", $"hausmeisteraufgaben-{year}.csv");
        }).RequireAuthorization(Policies.Admin);

        endpoints.MapGet("/verwaltung/tickets/routinen.pdf", async (ICareRoutines routines, TimeProvider clock, HttpContext context, int? jahr) =>
        {
            var year = jahr ?? GermanTime.Today(clock.GetUtcNow()).Year;
            if (year is < 2000 or > 2100)
            {
                return Results.BadRequest();
            }

            var pdf = await routines.ProofPdfAsync(context.User, year, context.RequestAborted);
            context.Response.Headers.CacheControl = "no-store";
            return Results.File(pdf, "application/pdf", $"hausmeisteraufgaben-{year}.pdf");
        }).RequireAuthorization(Policies.Admin);
        return endpoints;
    }

    internal static byte[] Csv(IEnumerable<(RoutineRow Routine, CheckRow Check)> rows)
    {
        var text = new StringBuilder();
        text.Append("Objekt;Aufgabe;Rhythmus;Erledigt am;Erledigt von;Notiz;Foto\r\n");
        foreach (var (routine, check) in rows)
        {
            text.AppendJoin(';',
                OpenItemsEndpoint.Field(routine.Property),
                OpenItemsEndpoint.Field(routine.Title),
                OpenItemsEndpoint.Field(Labels.From(routine.Interval)),
                check.DoneOn.ToString("dd.MM.yyyy", German),
                OpenItemsEndpoint.Field(check.DoneBy),
                OpenItemsEndpoint.Field(check.Note ?? ""),
                check.FileId is null ? "" : "ja").Append("\r\n");
        }

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text.ToString())];
    }
}
