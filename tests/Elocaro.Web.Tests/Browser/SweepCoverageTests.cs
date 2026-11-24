using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.AspNetCore.Routing.Template;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Web.Tests.Browser;

/// <summary>Every page of the administration is part of the page and field sweeps; a new page that is missing there fails here.</summary>
public class SweepCoverageTests(PostgresFixture postgres) : PageSweepBase(postgres)
{
    /// <summary>Pages the sweeps do not open on purpose, with the reason.</summary>
    private static readonly string[] Excluded =
    [
        "/verwaltung/einrichtung", // setup wizard: only reachable before the setup is completed
        "/verwaltung/personen/{id:guid}/auskunft", // the data subject access report as a file download
        "/verwaltung/system/datensicherung/hochladen", // upload endpoint of the backup page (form target)
        "/verwaltung/system/neuigkeiten/pruefen", // form target of the news page
        "/verwaltung/system/notfallmappe/erzeugen", // form target of the emergency folder page
        "/verwaltung/tickets/routinen/{Id:guid}", // needs a routine, which only a signed-in principal can create; the list above it is swept
    ];

    // Downloads (PDF, CSV, JSON, files of an export) are endpoints without a page to look at.
    private static bool IsFile(string pattern) =>
        pattern.EndsWith(".pdf", StringComparison.Ordinal) || pattern.EndsWith(".csv", StringComparison.Ordinal) || pattern.EndsWith(".json", StringComparison.Ordinal)
        || pattern.EndsWith("/datei", StringComparison.Ordinal) || pattern.EndsWith("/download", StringComparison.Ordinal) || pattern.EndsWith("/export", StringComparison.Ordinal);

    [Fact]
    public async Task Every_page_of_the_administration_is_part_of_the_sweeps()
    {
        await SignedInAsAdminAsync(mobile: false);
        await SeedAsync();
        var paths = Routes().Concat(OptionalRoutes()).Select(r => r.Split('?')[0]).Distinct().ToList();
        var patterns = App.Services.GetServices<EndpointDataSource>()
            .SelectMany(s => s.Endpoints).OfType<RouteEndpoint>()
            .Select(e => e.RoutePattern.RawText ?? "")
            .Where(p => p.StartsWith("/verwaltung", StringComparison.Ordinal) && !Excluded.Contains(p) && !IsFile(p))
            .Distinct().ToList();

        var missing = patterns.Where(pattern =>
        {
            var matcher = new TemplateMatcher(TemplateParser.Parse(pattern), new RouteValueDictionary());
            return !paths.Any(path => matcher.TryMatch(path, new RouteValueDictionary()));
        }).Order(StringComparer.Ordinal).ToList();

        Assert.True(patterns.Count > 50, "Die Routentabelle enthält zu wenige Seiten: Der Test prüft nichts.");
        Assert.True(missing.Count == 0, "Seiten ohne Durchlauf (Routen in PageSweepBase.Routes ergänzen):\n" + string.Join("\n", missing));
    }
}
