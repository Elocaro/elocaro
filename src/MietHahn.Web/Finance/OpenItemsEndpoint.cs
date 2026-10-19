using System.Globalization;
using System.Text;
using MietHahn.Application.Accounts;
using MietHahn.Application.Finance;
using MietHahn.Domain.Common;
using MietHahn.Web.Display;

namespace MietHahn.Web.Finance;

internal static class OpenItemsEndpoint
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>Offene Posten als CSV (Semikolon, deutsche Zahlen, UTF-8 mit BOM für Excel).</summary>
    public static IEndpointRouteBuilder MapOpenItemsEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/verwaltung/finanzen/offene-posten.csv", async (ITenantAccounts accounts, TimeProvider clock, HttpContext context) =>
        {
            var items = await accounts.OpenItemsAsync(context.RequestAborted);
            context.Response.Headers.CacheControl = "no-store";
            return Results.File(Csv(items), "text/csv; charset=utf-8", $"offene-posten-{GermanTime.Today(clock.GetUtcNow()):yyyy-MM-dd}.csv");
        }).RequireAuthorization(Policies.Finance);
        return endpoints;
    }

    internal static byte[] Csv(IEnumerable<OpenItem> items)
    {
        var text = new StringBuilder();
        // Zeilenende nach RFC 4180 unabhängig vom Betriebssystem.
        text.Append("Mietobjekt;Mieter;Art;Forderung;Fällig;Betrag;Offen;Tage überfällig\r\n");
        foreach (var p in items)
        {
            text.AppendJoin(';',
                Field(p.RentalUnit),
                Field(p.Tenant),
                Field(Labels.From(p.Kind)),
                Field(p.Text),
                p.Due.ToString("dd.MM.yyyy", German),
                p.Amount.ToString("0.00", German),
                p.Open.ToString("0.00", German),
                p.DaysOverdue.ToString(German)).Append("\r\n");
        }

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text.ToString())];
    }

    /// <summary>Quotiert bei Bedarf und entschärft Formeln (CSV-Injection).</summary>
    private static string Field(string value)
    {
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            value = "'" + value;
        }

        return value.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : value;
    }
}
