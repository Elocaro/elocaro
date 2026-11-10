namespace Elocaro.Web.Components.Common;

/// <summary>Explanations of the technical terms: the help page lists them, <see cref="HelpTerm"/> shows them next to the place they are used.</summary>
public static class Glossary
{
    public static IReadOnlyList<(string Term, string Text)> Tenant { get; } =
    [
        ("Kaltmiete", "Die Miete für die Wohnung selbst, ohne Nebenkosten."),
        ("Betriebskosten (Nebenkosten)", "Kosten für das Haus, die der Vermieter auf die Mieter umlegen darf, etwa Wasser, Müllabfuhr, Hausmeister. Sie zahlen monatlich eine Vorauszahlung, einmal im Jahr folgt die Abrechnung."),
        ("Vorauszahlung", "Der monatliche Abschlag auf die Betriebs- oder Heizkosten. Die Abrechnung zeigt, ob Sie nachzahlen müssen oder Geld zurückbekommen."),
        ("Saldo", "Der Stand Ihres Mieterkontos: Ein positiver Betrag ist noch offen, ein negativer ist ein Guthaben."),
        ("Kaution", "Die Sicherheit, die Sie zu Beginn hinterlegen. Sie bekommt der Vermieter nach Mietende abzüglich berechtigter Forderungen mit Zinsen zurück."),
        ("Verbrauchsinformation", "Die monatliche Übersicht Ihres Heiz- und Warmwasserverbrauchs mit Vergleich zum Vormonat und Vorjahr."),
    ];

    public static IReadOnlyList<(string Term, string Text)> Landlord { get; } =
    [
        ("Sollstellung", "Eine Forderung an den Mieter, die fällig werden soll, meist die monatliche Miete. Elocaro erzeugt sie automatisch aus dem Vertrag; Zahlungen werden ihr zugeordnet."),
        ("Soll und Haben", "Soll ist, was der Mieter zahlen muss (Miete, Vorauszahlungen). Haben ist, was er gezahlt hat. Soll minus Haben ergibt den Saldo."),
        ("Gesamtschuldnerisch", "Mehrere Mieter haften gemeinsam für die ganze Miete; der Vermieter darf sich an jeden von ihnen halten."),
        ("Offener Posten", "Eine Sollstellung, die noch nicht (vollständig) bezahlt ist."),
        ("Saldovortrag", "Der Stand des Mieterkontos zum Start der Buchhaltung in Elocaro, wenn Sie Verträge aus einer früheren Verwaltung übernehmen."),
        ("Umlageschlüssel", "Die Regel, nach der ein Kostenblock auf die Wohnungen verteilt wird, etwa nach Wohnfläche, Personenzahl, Verbrauch oder Einheiten."),
        ("Abrechnungszeitraum", "Meist ein Kalenderjahr. Die Betriebskostenabrechnung muss dem Mieter spätestens zwölf Monate nach Ende des Zeitraums zugehen."),
        ("Heizkostenverordnung", "Regelt, dass Heiz- und Warmwasserkosten überwiegend (50 bis 70 %) nach Verbrauch abgerechnet werden."),
        ("Staffelmiete / Indexmiete", "Staffelmiete: Die Erhöhungen stehen schon im Vertrag. Indexmiete: Die Miete folgt dem Verbraucherpreisindex."),
        ("Kappungsgrenze", "Bei einer Mieterhöhung auf die ortsübliche Vergleichsmiete darf die Miete innerhalb von drei Jahren höchstens um 20 % (in manchen Gebieten 15 %) steigen."),
        ("Mietspiegel", "Übersicht der ortsüblichen Mieten einer Gemeinde. Grundlage für Mieterhöhungen nach § 558 BGB."),
        ("Anlage V", "Die Steuererklärung für Einkünfte aus Vermietung. Elocaro zeigt die Werte zeilenweise an."),
        ("Mahnung", "Schreiben bei Zahlungsverzug. Elocaro schlägt Mahnungen nur vor; versendet wird erst nach Ihrer Freigabe."),
        ("Zufluss", "Für die Steuer zählt das Jahr, in dem das Geld tatsächlich eingegangen ist, nicht das Jahr, für das es geschuldet war."),
        ("Bemessungsgrundlage", "Der Betrag, von dem die Abschreibung berechnet wird: die Anschaffungs- oder Herstellungskosten des Gebäudes ohne den Grundstücksanteil."),
        ("Verrechnungsreihenfolge", "Die Regel, welche offene Forderung eine Zahlung zuerst tilgt, wenn der Mieter nichts anderes bestimmt hat. Einstellbar unter Forderungen → Einstellungen."),
        ("Emissionsfaktor", "Die CO₂-Menge je Kilowattstunde des Brennstoffs. Er geht in die Aufteilung der CO₂-Kosten zwischen Vermieter und Mieter ein."),
        ("Sachkontenlänge", "Die Stellenzahl der Konten in Ihrer Buchhaltung, meist vier. Ihr Steuerbüro nennt Ihnen den passenden Wert."),
    ];

    /// <summary>The explanation of a term. An unknown term is a programming error and fails the page's test instead of showing an empty note.</summary>
    public static string Explain(string term) =>
        Tenant.Concat(Landlord).First(entry => entry.Term == term).Text;
}
