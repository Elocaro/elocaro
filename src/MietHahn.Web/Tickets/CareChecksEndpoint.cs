using System.Globalization;
using System.Text;
using MietHahn.Application.Accounts;
using MietHahn.Application.Tickets;
using MietHahn.Domain.Common;
using MietHahn.Web.Display;
using MietHahn.Web.Finance;

namespace MietHahn.Web.Tickets;

internal static class CareChecksEndpoint
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>Proof of the caretaker duties of a year as CSV (semicolon, UTF-8 with BOM for Excel).</summary>
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
        return endpoints;
    }

    internal static byte[] Csv(IEnumerable<(RoutineRow Routine, CheckRow Check)> rows)
    {
        var text = new StringBuilder();
        text.Append("Objekt;Aufgabe;Rhythmus;Erledigt am;Erledigt von;Notiz\r\n");
        foreach (var (routine, check) in rows)
        {
            text.AppendJoin(';',
                OpenItemsEndpoint.Field(routine.Property),
                OpenItemsEndpoint.Field(routine.Title),
                OpenItemsEndpoint.Field(Labels.From(routine.Interval)),
                check.DoneOn.ToString("dd.MM.yyyy", German),
                OpenItemsEndpoint.Field(check.DoneBy),
                OpenItemsEndpoint.Field(check.Note ?? "")).Append("\r\n");
        }

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text.ToString())];
    }
}
