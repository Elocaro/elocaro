using Elocaro.Web.Display;

namespace Elocaro.Web.Tests.Operations;

/// <summary>Release notes come from GitHub: Markdown is rendered, everything dangerous stays text or disappears.</summary>
public class ReleaseMarkdownTests
{
    private static string Html(string markdown) => ReleaseMarkdown.ToMarkup(markdown).Value;

    [Fact]
    public void Inline_markup_and_nested_lists_are_rendered()
    {
        var html = Html("### Geändert\n- **Fett**, *kursiv*, `code`\n  - Unterpunkt");

        Assert.Contains("<h3>Geändert</h3>", html, StringComparison.Ordinal);
        Assert.Contains("<strong>Fett</strong>", html, StringComparison.Ordinal);
        Assert.Contains("<em>kursiv</em>", html, StringComparison.Ordinal);
        Assert.Contains("<code>code</code>", html, StringComparison.Ordinal);
        Assert.Contains("<li>Unterpunkt</li>", html, StringComparison.Ordinal);
        Assert.Contains("<ul>", html[html.IndexOf("<li>", StringComparison.Ordinal)..][4..], StringComparison.Ordinal);
    }

    [Fact]
    public void Raw_html_images_and_unsafe_links_are_not_rendered()
    {
        var html = Html("<script>alert(1)</script> ![x](https://example.test/a.png) [a](javascript:alert(1)) [b](https://example.test/b)");

        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<img", html, StringComparison.Ordinal);
        Assert.DoesNotContain("javascript:", html, StringComparison.Ordinal);
        Assert.Contains("href=\"https://example.test/b\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Headings_above_level_three_are_dropped()
    {
        var html = Html("## [0.5.0] - 2026-11-13\n\n### Hinzugefügt\n- Etwas");

        Assert.DoesNotContain("<h2", html, StringComparison.Ordinal);
        Assert.Contains("<h3>Hinzugefügt</h3>", html, StringComparison.Ordinal);
    }
}
