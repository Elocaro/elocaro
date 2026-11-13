using Markdig;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.AspNetCore.Components;

namespace Elocaro.Web.Display;

/// <summary>
/// Renders the Markdown of a release note (it comes from GitHub, so it is untrusted) to safe HTML: no raw HTML,
/// no images, links only with http(s), no headings above level 3 (the page has its own).
/// </summary>
public static class ReleaseMarkdown
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
        .UseEmphasisExtras()
        .Build();

    public static MarkupString ToMarkup(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        var document = Markdown.Parse(markdown, Pipeline);
        foreach (var heading in document.Descendants<HeadingBlock>().Where(h => h.Level <= 2).ToList())
        {
            heading.Parent?.Remove(heading);
        }

        foreach (var link in document.Descendants<LinkInline>().ToList())
        {
            if (link.IsImage)
            {
                ReplaceByContent(link);
            }
            else if (!IsWebAddress(link.Url))
            {
                link.Url = null;
            }
            else
            {
                link.GetAttributes().AddPropertyIfNotExist("rel", "noopener");
            }
        }

        return new MarkupString(Markdown.ToHtml(document, Pipeline));
    }

    private static bool IsWebAddress(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    private static void ReplaceByContent(LinkInline link)
    {
        var parent = link.Parent;
        if (parent is null)
        {
            return;
        }

        var children = link.ToList();
        foreach (var child in children)
        {
            child.Remove();
            link.InsertBefore(child);
        }

        link.Remove();
    }
}
