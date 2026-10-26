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
        new("Anlage Hausordnung", TemplateKind.Snippet, "Platzhaltertext der Hausordnung als Anlage zum Mietvertrag; an das Haus anpassen", "", Read("annex-house-rules")),
        new("Anlage Energieausweis", TemplateKind.Snippet, "Hinweis zur Übergabe des Energieausweises (§ 80 GEG)", "", Read("annex-energy")),
        new("Anlage Datenschutzinformation", TemplateKind.Snippet, "Datenschutzinformation für Mieter nach Art. 13 DSGVO (Mustertext)", "", Read("annex-privacy")),
        new("Anlage Übergabeprotokoll", TemplateKind.Snippet, "Leeres Übergabeprotokoll zum Ausfüllen bei Einzug", "", Read("annex-handover")),
        new("Mietvertrag Wohnraum", TemplateKind.Contract, "Wohnraummietvertrag mit Staffel-/Indexmiete, Betriebskosten, Kaution und Anlagen", "Mietvertrag {{ unit_name }}, {{ property_name }}", Read("contract-lease")),
        new("Zimmervertrag WG", TemplateKind.Contract, "Mietvertrag über ein WG-Zimmer mit Gemeinschaftsflächen, Pauschale und Möblierungsliste", "Zimmervertrag {{ room_name }}, {{ unit_name }}", Read("contract-room")),
        new("Mieterhöhung Vergleichsmiete (§ 558 BGB)", TemplateKind.RentIncrease, "Zustimmungsverlangen mit Begründung (Mietspiegel, Vergleichswohnungen oder Gutachten), Kappungsgrenze, Fristen und Zustimmungserklärung", "Mieterhöhungsverlangen nach § 558 BGB", Read("rent-comparative")),
        new("Mieterhöhung Indexmiete (§ 557b BGB)", TemplateKind.RentIncrease, "Erklärung der Indexanpassung mit Indexständen und neuer Miete", "Mieterhöhung nach § 557b BGB (Indexmiete)", Read("rent-index")),
        new("Mieterhöhung Modernisierung (§ 559 BGB)", TemplateKind.RentIncrease, "Mieterhöhung nach Modernisierung mit Kostenaufstellung, Kappungsgrenze, Fristen und Härtehinweis", "Mieterhöhung nach Modernisierung (§ 559 BGB)", Read("rent-modernisation")),
        new("Information Staffelmiete (§ 557a BGB)", TemplateKind.RentIncrease, "Freiwillige Information über die nächste Staffel", "Information zur Staffelmiete", Read("rent-graduated")),
        new("Anpassung Betriebskostenpauschale (§ 560 BGB)", TemplateKind.RentIncrease, "Erhöhung der Pauschale bei vertraglichem Vorbehalt, mit Begründung", "Anpassung der Betriebskostenpauschale nach § 560 BGB", Read("rent-flat-rate")),
        new("Ankündigung von Arbeiten", TemplateKind.Letter, "Serienbrief an die Mieter eines Objekts; Art und Zeitraum ergänzen", "Ankündigung von Arbeiten am Haus {{ property_name }}", Read("letter-announcement")),
        new("Änderung des Umlageschlüssels (§ 556a Abs. 2 BGB)", TemplateKind.Letter, "Erklärung in Textform vor Beginn des Abrechnungszeitraums, z. B. Müll künftig nach Personen; Kostenart, Jahr und Begründung ergänzen", "Änderung des Umlageschlüssels {{ rental_unit }}", Read("letter-allocation-key")),
        new("Wohnungsgeberbestätigung Einzug (§ 19 BMG)", TemplateKind.Letter, "Bestätigung für das Einwohnermeldeamt, zwei Wochen nach dem Einzug auszustellen", "Wohnungsgeberbestätigung (Einzug) {{ rental_unit }}", Read("letter-landlord-confirmation-move-in")),
        new("Wohnungsgeberbestätigung Auszug (§ 19 BMG)", TemplateKind.Letter, "Bestätigung für das Einwohnermeldeamt, zwei Wochen nach dem Auszug auszustellen", "Wohnungsgeberbestätigung (Auszug) {{ rental_unit }}", Read("letter-landlord-confirmation-move-out")),
        new("Bestätigung der Kündigung", TemplateKind.Letter, "Bestätigt den Eingang der Mieterkündigung mit Ende des Mietverhältnisses und Hinweisen zur Übergabe", "Bestätigung Ihrer Kündigung {{ rental_unit }}", Read("letter-termination-confirmation")),
        new("Mietbescheinigung", TemplateKind.Letter, "Bescheinigt Miete, Nebenkosten und Wohnfläche, z. B. für Wohngeld oder Behörden", "Mietbescheinigung {{ rental_unit }}", Read("letter-rent-certificate")),
        new("Mietschuldenfreiheitsbescheinigung", TemplateKind.Letter, "Bescheinigt, dass keine Mietrückstände bestehen (Saldo aus dem Mieterkonto)", "Bescheinigung über Mietschuldenfreiheit {{ rental_unit }}", Read("letter-no-arrears")),
        new("Kautionsabrechnung und -rückzahlung", TemplateKind.Letter, "Stellt Eingang, Zinsen, Verrechnungen und den zu erstattenden Betrag der Kaution dar", "Abrechnung Ihrer Mietkaution {{ rental_unit }}", Read("letter-deposit-refund")),
        new("Allgemeines Schreiben", TemplateKind.Letter, "Leerer Brief mit Anrede und Grußformel", "Mitteilung zu Ihrem Mietverhältnis", Read("letter-general")),
    ];

    private static string Read(string name) => EmbeddedResource.ReadText($"Documents/Templates/{name}.sbn").ReplaceLineEndings("\n").TrimEnd() + "\n";
}
