namespace MietHahn.Domain.Receipts;

/// <summary>Cost type with a fixed key; the operating cost types follow § 2 BetrKV.</summary>
/// <param name="BetrKvNumber">Number under § 2 BetrKV, <c>null</c> for non-allocable costs.</param>
public sealed record CostType(string Key, string Name, bool Allocable, int? BetrKvNumber = null, string? Hint = null);

/// <summary>
/// Master catalog of cost types. The keys are stored and must not change; custom subtypes
/// (e.g. "Gebäudeversicherung" under no. 13) come with the per-property cost type catalog (#28).
/// </summary>
public static class CostTypes
{
    /// <summary>Cost type of maintenance expenses (Erhaltungsaufwand), which can be spread over several years (§ 82b EStDV).</summary>
    public const string MaintenanceKey = "instandhaltung";

    public static IReadOnlyList<CostType> All { get; } =
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
        new(MaintenanceKey, "Instandhaltung und Reparaturen", false, Hint: "Erhaltungsaufwand: sofort abziehbar oder auf 2–5 Jahre verteilbar (§ 82b EStDV)."),
        new("modernisierung", "Modernisierung / Herstellungskosten", false, Hint: "Steuerlich ggf. über die AfA statt sofort."),
        new("verwaltung", "Verwaltungskosten", false),
        new("fahrtkosten", "Fahrtkosten", false),
        new("bankgebuehren", "Kontoführung und Bankgebühren", false),
        new("schuldzinsen", "Schuldzinsen", false),
        new("beratung", "Rechts- und Steuerberatung", false),
        new("leerstand", "Kosten bei Leerstand", false, Hint: "Betriebskosten leerstehender Einheiten trägt der Vermieter."),
        new("sonstiges", "Sonstige Kosten (nicht umlagefähig)", false),
    ];

    private static readonly Dictionary<string, CostType> ByKey = All.ToDictionary(k => k.Key, StringComparer.Ordinal);

    public static CostType? Find(string key) => ByKey.GetValueOrDefault(key);

    public static bool IsKnown(string key) => ByKey.ContainsKey(key);
}
