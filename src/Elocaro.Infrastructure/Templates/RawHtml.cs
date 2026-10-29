namespace Elocaro.Infrastructure.Templates;

/// <summary>HTML that is already safe and is not escaped when rendering (e.g. the content in the layout).</summary>
public sealed record RawHtml(string Html)
{
    public override string ToString() => Html;
}
