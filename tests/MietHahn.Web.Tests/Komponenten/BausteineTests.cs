using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Web.Anzeige;
using MietHahn.Web.Components.Gemeinsam;

namespace MietHahn.Web.Tests.Komponenten;

public class BausteineTests : BunitContext
{
    [Theory]
    [InlineData("1.234,56", 1234.56)]
    [InlineData("1234,56", 1234.56)]
    [InlineData("1234.56", 1234.56)]
    [InlineData("1234.5", 1234.5)]
    [InlineData("-12 €", -12)]
    [InlineData("812,505", 812.51)]
    [InlineData("1.234", 1234)] // Tausenderpunkt ohne Nachkommastellen
    [InlineData(" 0,5 ", 0.5)]
    public void Betraege_werden_deutsch_gelesen(string text, double erwartet)
    {
        Assert.True(InputBetrag.Lesen(text, out var betrag));
        Assert.Equal((decimal)erwartet, betrag);
    }

    [Theory]
    [InlineData("zwölf")]
    [InlineData("1,2,3")]
    public void Ungueltige_Betraege_werden_erkannt(string text)
    {
        Assert.False(InputBetrag.Lesen(text, out _));
    }

    [Fact]
    public void Leerer_Betrag_ist_null()
    {
        Assert.True(InputBetrag.Lesen("  ", out var betrag));
        Assert.Null(betrag);
    }

    [Fact]
    public void Sortierkopf_schaltet_Richtung_um_und_setzt_aria_sort()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("/verwaltung/benutzer?seite=3");

        var neu = Render<SortierKopf>(p => p.Add(x => x.Spalte, "name").Add(x => x.Text, "Name").Add(x => x.Abfrage, new Tabellenabfrage(null, false, 3)));
        var aufsteigend = Render<SortierKopf>(p => p.Add(x => x.Spalte, "name").Add(x => x.Text, "Name").Add(x => x.Abfrage, new Tabellenabfrage("name", false, 1)));

        Assert.Equal("none", neu.Find("th").GetAttribute("aria-sort"));
        Assert.Equal("http://localhost/verwaltung/benutzer?sortierung=name", neu.Find("a").GetAttribute("href"));
        Assert.Equal("ascending", aufsteigend.Find("th").GetAttribute("aria-sort"));
        Assert.Contains("absteigend=true", aufsteigend.Find("a").GetAttribute("href"), StringComparison.Ordinal);
    }

    [Fact]
    public void Seitenwahl_erscheint_nur_bei_mehreren_Seiten()
    {
        Assert.Empty(Render<Seitenwahl>(p => p.Add(x => x.Seite, 1).Add(x => x.Seiten, 1)).Markup.Trim());

        var mitte = Render<Seitenwahl>(p => p.Add(x => x.Seite, 2).Add(x => x.Seiten, 3).Add(x => x.Gesamt, 60));
        var links = mitte.FindAll("a").Select(a => a.GetAttribute("href")).ToList();
        Assert.Equal(["http://localhost/", "http://localhost/?seite=3"], links);
        Assert.Contains("Seite 2 von 3 (60 Einträge)", mitte.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Tabellenabfrage_sortiert_und_blaettert()
    {
        var zahlen = Enumerable.Range(1, 30).ToList();
        var abfrage = new Tabellenabfrage("wert", Absteigend: true, Seite: 2, SeitenGroesse: 10);

        var sortiert = abfrage.Sortieren(zahlen, new Dictionary<string, Func<int, object?>> { ["wert"] = z => z }, "wert").ToList();
        var seite = abfrage.Blaettern(sortiert);

        Assert.Equal(Enumerable.Range(11, 10).Reverse(), seite.Eintraege);
        Assert.Equal(3, seite.Seiten);
        Assert.Equal(30, seite.Gesamt);
    }

    [Fact]
    public void Unbekannte_Sortierspalte_faellt_auf_Standard_zurueck()
    {
        var abfrage = new Tabellenabfrage("boese'--", false, 1);

        var sortiert = abfrage.Sortieren(new List<int> { 3, 1, 2 }.AsQueryable(), new Dictionary<string, System.Linq.Expressions.Expression<Func<int, object?>>> { ["wert"] = z => z }, "wert");

        Assert.Equal([1, 2, 3], sortiert);
    }

    [Fact]
    public void Seitenkopf_zeigt_Titel_Zurueck_und_Aktionen()
    {
        var kopf = Render<Seitenkopf>(p => p
            .Add(x => x.Titel, "Benutzer")
            .Add(x => x.Zurueck, "/verwaltung")
            .Add(x => x.Aktionen, (RenderFragment)(b => b.AddMarkupContent(0, "<a class=\"knopf\" href=\"/neu\">Neu</a>"))));

        Assert.Equal("Benutzer", kopf.Find("h1").TextContent);
        Assert.Equal("/verwaltung", kopf.Find("a.zurueck").GetAttribute("href"));
        Assert.NotNull(kopf.Find(".seitenkopf-aktionen a.knopf"));
    }

    [Fact]
    public void Gefahrenaktion_fragt_vor_dem_Absenden_nach()
    {
        var aktion = Render<GefahrenAktion>(p => p
            .Add(x => x.Text, "Konto sperren")
            .Add(x => x.Rueckfrage, "Wirklich sperren?")
            .Add(x => x.FormName, "sperren")
            .Add(x => x.Bestaetigt, () => { }));

        var details = aktion.Find("details.gefahrenaktion");
        Assert.False(details.HasAttribute("open"));
        Assert.Equal("Konto sperren", details.QuerySelector("summary")!.TextContent);
        Assert.Contains("Wirklich sperren?", details.TextContent, StringComparison.Ordinal);
    }
}
