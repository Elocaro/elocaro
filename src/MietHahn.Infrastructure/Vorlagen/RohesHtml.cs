namespace MietHahn.Infrastructure.Vorlagen;

/// <summary>Bereits sicheres HTML, das beim Rendern nicht escaped wird (z. B. der Inhalt im Layout).</summary>
public sealed record RohesHtml(string Html)
{
    public override string ToString() => Html;
}
