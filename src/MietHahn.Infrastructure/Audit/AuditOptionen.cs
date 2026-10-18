namespace MietHahn.Infrastructure.Audit;

/// <summary>Konfigurationsabschnitt <c>Audit</c>.</summary>
public sealed class AuditOptionen
{
    public const string Abschnitt = "Audit";

    /// <summary>Lesende Zugriffe auf personenbezogene Daten protokollieren.</summary>
    public bool ZugriffeProtokollieren { get; set; } = true;

    /// <summary>
    /// Aufbewahrung der Änderungen in Jahren. Standard 10 Jahre, angelehnt an steuerliche Aufbewahrungsfristen
    /// (Abrechnungen, Belege). Ältere Einträge löscht ein täglicher Job.
    /// </summary>
    public int AufbewahrungJahre { get; set; } = 10;

    /// <summary>Aufbewahrung von Zugriffsprotokollen in Monaten (Datensparsamkeit).</summary>
    public int ZugriffeAufbewahrungMonate { get; set; } = 12;
}
