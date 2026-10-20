using System.Globalization;
using System.Text;
using MietHahn.Application.Accounts;
using MietHahn.Application.Documents;
using MietHahn.Application.Tax;
using MietHahn.Domain.Tax;

namespace MietHahn.Web.Finance;

internal static class AnlageVEndpoints
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>Filling aid for the Anlage V as CSV (line, designation, amount) and PDF.</summary>
    public static IEndpointRouteBuilder MapAnlageVEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/verwaltung/objekte/{propertyId:guid}/anlage-v.csv", async (Guid propertyId, int jahr, bool? prognose, IAnlageVService service, HttpContext context) =>
        {
            if (await service.GetAsync(propertyId, jahr, prognose ?? false, context.RequestAborted) is not { } report)
            {
                return Results.NotFound();
            }

            context.Response.Headers.CacheControl = "private, no-store";
            return Results.File(Csv(report), "text/csv; charset=utf-8", $"anlage-v-{jahr}.csv");
        }).RequireAuthorization(Policies.Finance);

        endpoints.MapGet("/verwaltung/objekte/{propertyId:guid}/anlage-v.pdf", async (Guid propertyId, int jahr, bool? prognose, IAnlageVService service, IPdfGenerator pdf, HttpContext context) =>
        {
            if (await service.GetAsync(propertyId, jahr, prognose ?? false, context.RequestAborted) is not { } report)
            {
                return Results.NotFound();
            }

            var bytes = await pdf.GenerateAsync(new PdfRequest("anlage-v", $"Anlage V {jahr} – {report.PropertyName}", Model(report)), context.RequestAborted);
            context.Response.Headers.XContentTypeOptions = "nosniff";
            context.Response.Headers.CacheControl = "private, no-store";
            return Results.File(bytes, "application/pdf", $"anlage-v-{jahr}.pdf");
        }).RequireAuthorization(Policies.Finance);
        return endpoints;
    }

    internal static byte[] Csv(AnlageVReport report)
    {
        var text = new StringBuilder();
        text.Append("Zeile;Bezeichnung;Betrag\r\n");
        void Append(string line, string label, decimal amount) =>
            text.AppendJoin(';', line, OpenItemsEndpoint.Field(label), amount.ToString("0.00", German)).Append("\r\n");

        foreach (var l in report.Result.Income)
        {
            Append(l.LineNumber.ToString(German), l.Info.Label, l.Amount);
        }

        Append("", "Summe der Einnahmen", report.Result.IncomeTotal);
        foreach (var l in report.Result.Expenses)
        {
            Append(l.LineNumber.ToString(German), l.Info.Label, l.Amount);
        }

        Append("", "Summe der Werbungskosten", report.Result.ExpenseTotal);
        if (report.Result.Reduction != 0)
        {
            Append("", "Kürzung wegen verbilligter Vermietung", -report.Result.Reduction);
        }

        Append("", "Überschuss", report.Result.Surplus);
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text.ToString())];
    }

    private static object Model(AnlageVReport report)
    {
        static object Line(AnlageVLineResult l) => new { line = l.LineNumber, label = l.Info.Label, amount = l.Amount };
        return new
        {
            property = report.PropertyName,
            year = report.Year,
            warnings = report.Warnings,
            income = report.Result.Income.Select(Line).ToList(),
            expenses = report.Result.Expenses.Select(Line).ToList(),
            income_total = report.Result.IncomeTotal,
            expense_total = report.Result.ExpenseTotal,
            reduction = report.Result.Reduction,
            deductible_expenses = report.Result.DeductibleExpenses,
            surplus = report.Result.Surplus,
            details = report.Result.Income.Concat(report.Result.Expenses)
                .SelectMany(l => l.Postings.Select(p => new { line = l.LineNumber, date = p.Date, text = p.Text, amount = p.Amount }))
                .ToList(),
        };
    }
}
