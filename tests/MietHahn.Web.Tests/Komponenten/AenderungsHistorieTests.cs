using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Audit;
using MietHahn.Web.Components.Gemeinsam;

namespace MietHahn.Web.Tests.Komponenten;

public class AenderungsHistorieTests : BunitContext
{
    private static readonly Guid Id = Guid.CreateVersion7();

    [Fact]
    public void Zeigt_Eintraege_mit_Aktion_Benutzer_und_Feldaenderungen()
    {
        Services.AddSingleton<IAenderungsHistorie>(new FesteHistorie(
            new AuditEintragAnsicht(new DateTimeOffset(2026, 3, 1, 9, 30, 0, TimeSpan.Zero), "partnerin", AuditAktion.Geaendert,
                [new Feldaenderung("Kaltmiete", "650", "700")], null),
            new AuditEintragAnsicht(new DateTimeOffset(2026, 2, 1, 8, 0, 0, TimeSpan.Zero), "admin", AuditAktion.Gelesen, [], "Mieterakte geöffnet")));

        var komponente = Render<AenderungsHistorie>(p => p.Add(x => x.EntitaetTyp, "Mietvertrag").Add(x => x.EntitaetId, Id));

        Assert.Contains("Änderungshistorie (2)", komponente.Find("summary").TextContent, StringComparison.Ordinal);
        var eintraege = komponente.FindAll("ol.historie-liste > li");
        Assert.Equal(2, eintraege.Count);
        Assert.Contains("01.03.2026 10:30", eintraege[0].TextContent, StringComparison.Ordinal); // MEZ
        Assert.Contains("geändert", eintraege[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("partnerin", eintraege[0].TextContent, StringComparison.Ordinal);
        var zellen = eintraege[0].QuerySelectorAll("tbody td").Select(z => z.TextContent).ToList();
        Assert.Equal(["Kaltmiete", "650", "700"], zellen);
        Assert.Contains("eingesehen", eintraege[1].TextContent, StringComparison.Ordinal);
        Assert.Contains("(Mieterakte geöffnet)", eintraege[1].TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Ohne_Eintraege_erscheint_ein_Hinweis()
    {
        Services.AddSingleton<IAenderungsHistorie>(new FesteHistorie());

        var komponente = Render<AenderungsHistorie>(p => p.Add(x => x.EntitaetTyp, "Mietvertrag").Add(x => x.EntitaetId, Id));

        Assert.Contains("Keine Einträge.", komponente.Markup, StringComparison.Ordinal);
    }

    private sealed class FesteHistorie(params AuditEintragAnsicht[] eintraege) : IAenderungsHistorie
    {
        public Task<IReadOnlyList<AuditEintragAnsicht>> LadenAsync(string entitaetTyp, Guid entitaetId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AuditEintragAnsicht>>(eintraege);
    }
}
