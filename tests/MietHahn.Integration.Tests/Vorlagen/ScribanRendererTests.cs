using MietHahn.Infrastructure.Vorlagen;

namespace MietHahn.Integration.Tests.Vorlagen;

public class ScribanRendererTests
{
    [Fact]
    public void Modellwerte_werden_HTML_escaped()
    {
        var ergebnis = ScribanRenderer.RendernText("<p>{{ name }}</p>", new { Name = "<script>alert(1)</script> & Co" });

        Assert.Equal("<p>&lt;script&gt;alert(1)&lt;/script&gt; &amp; Co</p>", ergebnis.Html);
    }

    [Fact]
    public void RohesHtml_wird_nicht_escaped()
    {
        var ergebnis = ScribanRenderer.RendernText("<div>{{ inhalt }}</div>", null, new Dictionary<string, object?> { ["inhalt"] = new RohesHtml("<b>fett</b>") });

        Assert.Equal("<div><b>fett</b></div>", ergebnis.Html);
    }

    [Fact]
    public void Eigenschaften_sind_in_snake_case_verfuegbar()
    {
        var ergebnis = ScribanRenderer.RendernText("{{ mieter_name }}", new { MieterName = "Erika Mustermann" });

        Assert.Equal("Erika Mustermann", ergebnis.Html);
    }

    [Fact]
    public void Captures_sind_als_Klartext_lesbar()
    {
        var ergebnis = ScribanRenderer.RendernText("{{ capture betreff }} Abrechnung für {{ name }} {{ end }}Text", new { Name = "Müller & Söhne" });

        Assert.Equal("Abrechnung für Müller & Söhne", ergebnis.Variable("betreff"));
        Assert.Null(ergebnis.Variable("text"));
        Assert.Equal("Text", ergebnis.Html);
    }

    [Fact]
    public void Unbekannte_Variable_ist_ein_Fehler()
    {
        var fehler = Assert.Throws<VorlagenFehlerException>(() => ScribanRenderer.RendernText("{{ tippfehler }}", new { Name = "x" }));

        Assert.Contains("tippfehler", fehler.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Syntaxfehler_werden_gemeldet()
    {
        Assert.NotEmpty(ScribanRenderer.Pruefen("{{ if }}"));
        Assert.Empty(ScribanRenderer.Pruefen("{{ if true }}ja{{ end }}"));
        Assert.Throws<VorlagenFehlerException>(() => ScribanRenderer.RendernText("{{ for }}", null));
    }

    [Theory]
    [InlineData(1234.5, "1.234,50 €")]
    [InlineData(-12, "-12,00 €")]
    [InlineData(0.005, "0,01 €")]
    public void Geld_wird_deutsch_formatiert(decimal betrag, string erwartet)
    {
        Assert.Equal(erwartet, ScribanRenderer.RendernText("{{ geld betrag }}", new { Betrag = betrag }).Html);
    }

    [Fact]
    public void Datum_wird_deutsch_formatiert_auch_fuer_Zeitpunkte()
    {
        var modell = new
        {
            Stichtag = new DateOnly(2026, 12, 31),
            Zeitpunkt = new DateTimeOffset(2026, 12, 31, 23, 30, 0, TimeSpan.Zero), // in Deutschland schon 01.01.2027
        };

        var ergebnis = ScribanRenderer.RendernText("{{ datum stichtag }}|{{ datum zeitpunkt }}", modell);

        Assert.Equal("31.12.2026|01.01.2027", ergebnis.Html);
    }

    [Fact]
    public void Text_zu_html_erhaelt_Zeilenumbrueche_und_escaped()
    {
        var ergebnis = ScribanRenderer.RendernText("{{ text_zu_html notiz }}", new { Notiz = "Zeile 1\n<Zeile 2>" });

        Assert.Equal("Zeile 1<br>&lt;Zeile 2&gt;", ergebnis.Html);
    }

    [Fact]
    public void Eingebettete_Vorlage_wird_gefunden_und_unbekannte_gemeldet()
    {
        var renderer = new ScribanRenderer();

        var ergebnis = renderer.RendernEingebettet("Mail/Vorlagen/test.sbn", new { GesendetAm = new DateOnly(2026, 3, 1), AusgeloestVon = "admin" });

        Assert.Equal("Testnachricht von MietHahn", ergebnis.Variable("betreff"));
        Assert.Contains("01.03.2026", ergebnis.Html, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => renderer.RendernEingebettet("Mail/Vorlagen/gibt-es-nicht.sbn", null));
    }
}
