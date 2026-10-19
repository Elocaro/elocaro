namespace MietHahn.Infrastructure.Audit;

/// <summary>Konfigurationsabschnitt <c>Audit</c>.</summary>
public sealed class AuditOptions
{
    public const string Section = "Audit";

    /// <summary>Lesende Zugriffe auf personenbezogene Daten protokollieren.</summary>
    public bool LogAccess { get; set; } = true;

    /// <summary>
    /// Aufbewahrung der Änderungen in Jahren. Standard 10 Jahre, angelehnt an steuerliche Aufbewahrungsfristen
    /// (Abrechnungen, Belege). Ältere Einträge löscht ein täglicher Job.
    /// </summary>
    public int RetentionYears { get; set; } = 10;

    /// <summary>Aufbewahrung von Zugriffsprotokollen in Monaten (Datensparsamkeit).</summary>
    public int AccessLogRetentionMonths { get; set; } = 12;
}
