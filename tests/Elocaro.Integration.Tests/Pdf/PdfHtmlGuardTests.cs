using Elocaro.Infrastructure.Pdf;

namespace Elocaro.Integration.Tests.Pdf;

public class PdfHtmlGuardTests
{
    [Theory]
    [InlineData("a <iframe src=\"http://mailpit:8025/api/v1/info\" width=\"600\"></iframe> b", "iframe")]
    [InlineData("a <IFRAME/src=file:///etc/passwd></IFRAME> b", "iframe")]
    [InlineData("a <object data=\"http://intern/x\"></object> b", "object")]
    [InlineData("a <embed src=\"http://intern/x\"> b", "embed")]
    [InlineData("a <link rel=\"stylesheet\" href=\"http://intern/x.css\"> b", "<link")]
    [InlineData("a <base href=\"http://intern/\"> b", "<base")]
    [InlineData("a <meta http-equiv=\"refresh\" content=\"0;url=http://intern\"> b", "<meta")]
    [InlineData("a <video src=\"http://intern/v.mp4\"></video> b", "video")]
    public void Active_tags_are_removed(string html, string forbidden)
    {
        var result = PdfHtmlGuard.Sanitize(html);

        Assert.DoesNotContain(forbidden, result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("a", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Scripts_are_removed_with_their_content()
    {
        var result = PdfHtmlGuard.Sanitize("x<script>fetch('http://intern')</script>y<SCRIPT src=//evil/x.js></SCRIPT>z");

        Assert.Equal("xyz", result);
    }

    [Theory]
    [InlineData("<img src=\"http://127.0.0.1:5432/x.png\">")]
    [InlineData("<img src='http://intern/x.png'>")]
    [InlineData("<img src=http://intern/x.png>")]
    [InlineData("<img srcset=\"http://intern/x.png 2x\">")]
    [InlineData("<img src=\"file:///tmp/geheim.png\">")]
    [InlineData("<img src=\"//intern/x.png\">")]
    [InlineData("<body background=\"http://intern/b.png\">")]
    public void External_resources_are_removed(string html)
    {
        var result = PdfHtmlGuard.Sanitize(html);

        Assert.DoesNotContain("intern", result, StringComparison.Ordinal);
        Assert.DoesNotContain("127.0.0.1", result, StringComparison.Ordinal);
        Assert.DoesNotContain("file:", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Inline_images_are_kept() =>
        Assert.Equal("<img src=\"data:image/svg+xml;base64,AAAA\" width=\"90\">", PdfHtmlGuard.Sanitize("<img src=\"data:image/svg+xml;base64,AAAA\" width=\"90\">"));

    [Theory]
    [InlineData("<p style=\"background: url(http://intern/x.png)\">t</p>")]
    [InlineData("<p style=\"background: url('http://intern/x.png')\">t</p>")]
    [InlineData("<style>@import url(http://intern/x.css); p { color: red }</style>")]
    [InlineData("<style>@import 'http://intern/x.css';</style>")]
    [InlineData("<style>@font-face { src: url(\"http://intern/f.woff\") }</style>")]
    public void External_css_resources_are_removed(string html) =>
        Assert.DoesNotContain("intern", PdfHtmlGuard.Sanitize(html), StringComparison.Ordinal);

    [Fact]
    public void Normal_letter_markup_stays_untouched()
    {
        const string html = "<h2>Betreff</h2><p class=\"small\">Text <b>fett</b> <a href=\"https://example.de\">Link</a></p><table><tr><td style=\"width: 50%\">1</td></tr></table><div style=\"page-break-before: always\"></div>";

        Assert.Equal(html, PdfHtmlGuard.Sanitize(html));
    }
}
