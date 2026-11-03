using Elocaro.Web.Components.Layout;

namespace Elocaro.Web.Tests;

public class ReturnTargetTests
{
    [Theory]
    [InlineData("/verwaltung/objekte/abc/abrechnung?jahr=2025&schritt=2", "Zurück zur Abrechnung")]
    [InlineData("/verwaltung/vertraege/abc/auszug", "Zurück zum Mieterwechsel")]
    [InlineData("/verwaltung/objekte/abc/zaehler", "Zurück")]
    public void A_path_inside_the_administration_is_accepted_with_a_fitting_text(string path, string text)
    {
        var target = ReturnTarget.From(path);

        Assert.NotNull(target);
        Assert.Equal(path, target.Href);
        Assert.Equal(text, target.Text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://example.org/")]
    [InlineData("//example.org/verwaltung/")]
    [InlineData("/verwaltung//example.org")]
    [InlineData("/portal/start")]
    [InlineData("/verwaltung/x\\..\\evil")]
    [InlineData("/verwaltung/x:javascript")]
    [InlineData("/verwaltung/x\ny")]
    public void Anything_else_is_rejected(string? path) => Assert.Null(ReturnTarget.From(path));

    [Fact]
    public void A_path_that_is_too_long_is_rejected() =>
        Assert.Null(ReturnTarget.From("/verwaltung/" + new string('a', 400)));

    [Theory]
    [InlineData("/verwaltung/belege", "/verwaltung/belege?zurueck=%2Fverwaltung%2Fx%3Fa%3D1%26b%3D2")]
    [InlineData("/verwaltung/belege?jahr=2025", "/verwaltung/belege?jahr=2025&zurueck=%2Fverwaltung%2Fx%3Fa%3D1%26b%3D2")]
    public void The_target_is_appended_as_one_escaped_value(string href, string expected) =>
        Assert.Equal(expected, ReturnTarget.AppendTo(href, "/verwaltung/x?a=1&b=2"));
}
