using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;

namespace Elocaro.Web.Display;

/// <summary>Turns the inline Markdown of a release note (<c>**bold**</c>, <c>`code`</c>) into safe HTML: everything else stays text.</summary>
public static partial class ReleaseText
{
    public static MarkupString ToMarkup(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var encoded = WebUtility.HtmlEncode(text);
        encoded = CodePattern().Replace(encoded, "<code>$1</code>");
        encoded = BoldPattern().Replace(encoded, "<strong>$1</strong>");
        return new MarkupString(encoded);
    }

    [GeneratedRegex("`([^`]+)`", RegexOptions.None, matchTimeoutMilliseconds: 200)]
    private static partial Regex CodePattern();

    [GeneratedRegex(@"\*\*([^*]+)\*\*", RegexOptions.None, matchTimeoutMilliseconds: 200)]
    private static partial Regex BoldPattern();
}
