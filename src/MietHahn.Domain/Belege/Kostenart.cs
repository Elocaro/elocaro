namespace MietHahn.Domain.Belege;

/// <summary>Kostenart mit festem Schlüssel; die Betriebskostenarten folgen § 2 BetrKV.</summary>
/// <param name="BetrKvNummer">Nummer nach § 2 BetrKV, bei nicht umlagefähigen Kosten <c>null</c>.</param>
public sealed record Kostenart(string Schluessel, string Bezeichnung, bool Umlagefaehig, int? BetrKvNummer = null, string? Hinweis = null);

/// <summary>
/// Stammkatalog der Kostenarten. Die Schlüssel werden gespeichert und dürfen sich nicht ändern; eigene Unterarten
/// (z. B. „Gebäudeversicherung“ unter Nr. 13) kommen mit dem Kostenarten-Katalog je Objekt (#28).
/// </summary>
public static class Kostenarten
{
    public static IReadOnlyList<Kostenart> Alle { get; } =
    [
        new("betrkv-01", "Laufende öffentliche Lasten (Grundsteuer)", true, 1),
        new("betrkv-02", "Wasserversorgung", true, 2),
        new("betrkv-03", "Entwässerung", true, 3),
        new("betrkv-04", "Heizung (Betrieb der Heizungsanlage)", true, 4, "Abrechnung nach HeizkostenV."),
        new("betrkv-05", "Warmwasserversorgung", true, 5, "Abrechnung nach HeizkostenV."),
        new("betrkv-06", "Verbundene Heizungs- und Warmwasseranlagen", true, 6, "Abrechnung nach HeizkostenV."),
        new("betrkv-07", "Aufzug", true, 7),
        new("betrkv-08", "Straßenreinigung und Müllbeseitigung", true, 8),
        new("betrkv-09", "Gebäudereinigung und Ungezieferbekämpfung", true, 9),
        new("betrkv-10", "Gartenpflege", true, 10),
        new("betrkv-11", "Beleuchtung (Allgemeinstrom)", true, 11),
        new("betrkv-12", "Schornsteinreinigung", true, 12),
        new("betrkv-13", "Sach- und Haftpflichtversicherung", true, 13),
        new("betrkv-14", "Hauswart", true, 14, "Ohne Anteile für Instandhaltung und Verwaltung."),
        new("betrkv-15", "Antenne / Breitbandnetz", true, 15, "Kabel-TV-Gebühren sind seit 1.7.2024 nicht mehr umlagefähig (TKG); umlagefähig bleiben u. a. Betrieb und Wartung einer Gemeinschaftsantenne."),
        new("betrkv-16", "Einrichtungen für die Wäschepflege", true, 16),
        new("betrkv-17", "Sonstige Betriebskosten", true, 17, "Nur wenn im Mietvertrag einzeln benannt (z. B. Dachrinnenreinigung, Wartung Rauchmelder)."),
        new("instandhaltung", "Instandhaltung und Reparaturen", false),
        new("modernisierung", "Modernisierung / Herstellungskosten", false, Hinweis: "Steuerlich ggf. über die AfA statt sofort."),
        new("verwaltung", "Verwaltungskosten", false),
        new("bankgebuehren", "Kontoführung und Bankgebühren", false),
        new("schuldzinsen", "Schuldzinsen", false),
        new("beratung", "Rechts- und Steuerberatung", false),
        new("leerstand", "Kosten bei Leerstand", false, Hinweis: "Betriebskosten leerstehender Einheiten trägt der Vermieter."),
        new("sonstiges", "Sonstige Kosten (nicht umlagefähig)", false),
    ];

    private static readonly Dictionary<string, Kostenart> NachSchluessel = Alle.ToDictionary(k => k.Schluessel, StringComparer.Ordinal);

    public static Kostenart? Finden(string schluessel) => NachSchluessel.GetValueOrDefault(schluessel);

    public static bool IstBekannt(string schluessel) => NachSchluessel.ContainsKey(schluessel);
}
