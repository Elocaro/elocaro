using MietHahn.Domain.Documents;
using MietHahn.Infrastructure.Templates;

namespace MietHahn.Infrastructure.Documents;

/// <summary>Bundled starting templates (embedded resources); a starting point, not legal advice.</summary>
internal static class TemplateDefaults
{
    public sealed record Entry(string Name, TemplateKind Kind, string? Description, string Subject, string Body);

    /// <summary>Name of the closing snippet that the bundled letters include.</summary>
    public const string Closing = "Grußformel";

    public static IReadOnlyList<Entry> All { get; } =
    [
        new(Closing, TemplateKind.Snippet, "Schlussformel mit Namen des Vermieters", "", Read("snippet-closing")),
        new("Bankverbindung", TemplateKind.Snippet, "Bankverbindung des Vermieters, nur wenn im Vermieterprofil hinterlegt", "", Read("snippet-bank-details")),
        new("Ankündigung von Arbeiten", TemplateKind.Letter, "Serienbrief an die Mieter eines Objekts; Art und Zeitraum ergänzen", "Ankündigung von Arbeiten am Haus {{ property_name }}", Read("letter-announcement")),
        new("Allgemeines Schreiben", TemplateKind.Letter, "Leerer Brief mit Anrede und Grußformel", "Mitteilung zu Ihrem Mietverhältnis", Read("letter-general")),
    ];

    private static string Read(string name) => EmbeddedResource.ReadText($"Documents/Templates/{name}.sbn").ReplaceLineEndings("\n").TrimEnd() + "\n";
}
