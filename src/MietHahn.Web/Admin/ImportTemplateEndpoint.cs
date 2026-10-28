using MietHahn.Application.Accounts;
using MietHahn.Application.Admin;
using MietHahn.Application.Setup;

namespace MietHahn.Web.Admin;

internal static class ImportTemplateEndpoint
{
    /// <summary>The sample file for the spreadsheet import (semicolon, German numbers, UTF-8 with BOM for Excel).</summary>
    public static IEndpointRouteBuilder MapImportTemplateEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/verwaltung/einstellungen/import/vorlage.csv", (IDataImport import, HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Results.File(import.TemplateCsv(), "text/csv; charset=utf-8", "miethahn-import-vorlage.csv");
        }).RequireAuthorization(Policies.System);
        return endpoints;
    }
}
