using MietHahn.Infrastructure.Templates;

namespace MietHahn.Integration.Tests.Templates;

public class ScribanRendererTests
{
    [Fact]
    public void Model_values_are_HTML_escaped()
    {
        var result = ScribanRenderer.RenderText("<p>{{ name }}</p>", new { Name = "<script>alert(1)</script> & Co" });

        Assert.Equal("<p>&lt;script&gt;alert(1)&lt;/script&gt; &amp; Co</p>", result.Html);
    }

    [Fact]
    public void Umlauts_stay_readable_quotes_are_escaped()
    {
        var result = ScribanRenderer.RenderText("<a title=\"{{ name }}\">{{ name }}</a>", new { Name = "Jörg \"Straße\" O'Neil" });

        Assert.Equal("<a title=\"Jörg &quot;Straße&quot; O&#39;Neil\">Jörg &quot;Straße&quot; O&#39;Neil</a>", result.Html);
    }

    [Fact]
    public void Included_snippets_keep_their_markup_but_their_model_values_are_escaped()
    {
        var snippets = new Dictionary<string, string> { ["Gruss"] = "<p>Mit freundlichen Grüßen<br>{{ name }}</p>" };

        var result = ScribanRenderer.RenderText("<div>{{ include 'Gruss' }}</div>", new { Name = "<b>Hahn</b>" }, snippets: snippets);

        Assert.Equal("<div><p>Mit freundlichen Grüßen<br>&lt;b&gt;Hahn&lt;/b&gt;</p></div>", result.Html);
    }

    [Fact]
    public void Snippets_that_include_each_other_in_a_circle_fail_with_a_message_instead_of_overflowing()
    {
        var snippets = new Dictionary<string, string> { ["A"] = "a{{ include 'B' }}", ["B"] = "b{{ include 'A' }}" };

        var error = Assert.Throws<TemplateErrorException>(() => ScribanRenderer.RenderText("{{ include 'A' }}", null, snippets: snippets));

        Assert.Contains("im Kreis", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RawHtml_is_not_escaped()
    {
        var result = ScribanRenderer.RenderText("<div>{{ content }}</div>", null, new Dictionary<string, object?> { ["content"] = new RawHtml("<b>fett</b>") });

        Assert.Equal("<div><b>fett</b></div>", result.Html);
    }

    [Fact]
    public void Properties_are_available_in_snake_case()
    {
        var result = ScribanRenderer.RenderText("{{ tenant_name }}", new { TenantName = "Erika Mustermann" });

        Assert.Equal("Erika Mustermann", result.Html);
    }

    [Fact]
    public void Captures_are_readable_as_plain_text()
    {
        var result = ScribanRenderer.RenderText("{{ capture subject }} Abrechnung für {{ name }} {{ end }}Text", new { Name = "Müller & Söhne" });

        Assert.Equal("Abrechnung für Müller & Söhne", result.Variable("subject"));
        Assert.Null(result.Variable("text"));
        Assert.Equal("Text", result.Html);
    }

    [Fact]
    public void Unknown_variable_is_an_error()
    {
        var errors = Assert.Throws<TemplateErrorException>(() => ScribanRenderer.RenderText("{{ tippfehler }}", new { Name = "x" }));

        Assert.Contains("tippfehler", errors.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Syntax_errors_are_reported()
    {
        Assert.NotEmpty(ScribanRenderer.Validate("{{ if }}"));
        Assert.Empty(ScribanRenderer.Validate("{{ if true }}ja{{ end }}"));
        Assert.Throws<TemplateErrorException>(() => ScribanRenderer.RenderText("{{ for }}", null));
    }

    [Theory]
    [InlineData(1234.5, "1.234,50 €")]
    [InlineData(-12, "-12,00 €")]
    [InlineData(0.005, "0,01 €")]
    public void Money_is_formatted_in_German(decimal amount, string expected)
    {
        Assert.Equal(expected, ScribanRenderer.RenderText("{{ money amount }}", new { Amount = amount }).Html);
    }

    [Fact]
    public void Date_is_formatted_in_German_also_for_timestamps()
    {
        var model = new
        {
            ReferenceDate = new DateOnly(2026, 12, 31),
            Timestamp = new DateTimeOffset(2026, 12, 31, 23, 30, 0, TimeSpan.Zero), // already 1 Jan 2027 in Germany
        };

        var result = ScribanRenderer.RenderText("{{ date reference_date }}|{{ date timestamp }}", model);

        Assert.Equal("31.12.2026|01.01.2027", result.Html);
    }

    [Fact]
    public void Text_to_html_keeps_line_breaks_and_escapes()
    {
        var result = ScribanRenderer.RenderText("{{ text_to_html note }}", new { Note = "Zeile 1\n<Zeile 2>" });

        Assert.Equal("Zeile 1<br>&lt;Zeile 2&gt;", result.Html);
    }

    [Fact]
    public void Embedded_template_is_found_and_unknown_one_is_reported()
    {
        var renderer = new ScribanRenderer();

        var result = renderer.RenderEmbedded("Mail/Templates/test.sbn", new { SentAt = new DateOnly(2026, 3, 1), TriggeredBy = "admin" });

        Assert.Equal("Testnachricht von MietHahn", result.Variable("subject"));
        Assert.Contains("01.03.2026", result.Html, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => renderer.RenderEmbedded("Mail/Templates/gibt-es-nicht.sbn", null));
    }

    [Fact]
    public void Care_proof_template_lists_the_ticks_per_routine()
    {
        var model = new
        {
            Year = 2026,
            Properties = new[]
            {
                new
                {
                    Property = "Lindenweg 3",
                    Routines = new[]
                    {
                        new { Title = "Treppenhaus", Interval = "wöchentlich", Count = 1, Checks = new[] { new { DoneOn = new DateOnly(2026, 3, 2), DoneBy = "Max", Note = "sauber" } } },
                        new { Title = "Hof", Interval = "monatlich", Count = 0, Checks = Array.Empty<object>().Select(_ => new { DoneOn = new DateOnly(2026, 1, 1), DoneBy = "", Note = "" }).ToArray() },
                    },
                },
            },
        };

        var html = new ScribanRenderer().RenderEmbedded("Pdf/Templates/care-proof.sbn", model).Html;

        Assert.Contains("Lindenweg 3", html, StringComparison.Ordinal);
        Assert.Contains("02.03.2026", html, StringComparison.Ordinal);
        Assert.Contains("Max", html, StringComparison.Ordinal);
        Assert.Contains("Keine Eintragung in 2026", html, StringComparison.Ordinal);
    }
}
