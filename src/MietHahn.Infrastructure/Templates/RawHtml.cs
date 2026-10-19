namespace MietHahn.Infrastructure.Templates;

/// <summary>Bereits sicheres HTML, das beim Rendern nicht escaped wird (z. B. der Inhalt im Layout).</summary>
public sealed record RawHtml(string Html)
{
    public override string ToString() => Html;
}
