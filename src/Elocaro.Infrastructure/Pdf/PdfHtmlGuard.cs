using System.Text.RegularExpressions;

namespace Elocaro.Infrastructure.Pdf;

/// <summary>
/// Removes everything from template HTML that would make Chromium (in Gotenberg) load resources or run active content:
/// frames, scripts, external images/styles/fonts. Only inline <c>data:image/</c> sources remain (e.g. the portal QR code).
/// This keeps template authors from using the PDF renderer to reach internal hosts (SSRF); Gotenberg additionally has a deny list.
/// </summary>
internal static partial class PdfHtmlGuard
{

    public static string Sanitize(string html)
    {
        ArgumentNullException.ThrowIfNull(html);

        html = ScriptBlock().Replace(html, "");
        html = ActiveTag().Replace(html, "");
        html = ResourceAttribute().Replace(html, match => IsInlineImage(match.Groups["value"].Value) ? match.Value : "");
        html = CssUrl().Replace(html, match => IsInlineImage(match.Groups["value"].Value.Trim()) ? match.Value : "none");
        html = CssImport().Replace(html, "");
        return html;
    }

    private static bool IsInlineImage(string value) =>
        value.Trim().StartsWith("data:image/", StringComparison.OrdinalIgnoreCase);

    // <script>…</script> including content.
    [GeneratedRegex(@"<\s*script\b.*?<\s*/\s*script\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 2000)]
    private static partial Regex ScriptBlock();

    // Opening and closing tags that load or execute things.
    [GeneratedRegex(@"<\s*/?\s*(script|iframe|frame|frameset|object|embed|applet|link|base|meta|form|input|button|portal|audio|video|source|track)\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 2000)]
    private static partial Regex ActiveTag();

    // Attributes that make the browser fetch something. href stays (plain links are not fetched while rendering).
    [GeneratedRegex(@"\s(?:src|srcset|poster|background|data|action|formaction|xlink:href|ping)\s*=\s*(?:""(?<value>[^""]*)""|'(?<value>[^']*)'|(?<value>[^\s>]+))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 2000)]
    private static partial Regex ResourceAttribute();

    // url(...) in style attributes and <style> blocks.
    [GeneratedRegex(@"url\(\s*(?<q>[""']?)(?<value>[^)""']*)\k<q>\s*\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 2000)]
    private static partial Regex CssUrl();

    [GeneratedRegex(@"@import\b[^;{]*;?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 2000)]
    private static partial Regex CssImport();
}
