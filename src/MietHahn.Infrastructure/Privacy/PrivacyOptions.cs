namespace MietHahn.Infrastructure.Privacy;

/// <summary>Konfigurationsabschnitt <c>Datenschutz</c>.</summary>
public sealed class PrivacyOptions
{
    public const string Section = "Privacy";

    /// <summary>
    /// Aufbewahrung nach Ende der letzten Beziehung (z. B. Vertragsende), bevor die Anonymisierung vorgeschlagen wird.
    /// 10 Jahre entsprechen der längsten steuerlichen Frist (§ 147 AO).
    /// </summary>
    public int RetentionYears { get; set; } = 10;

    /// <summary>Personen ohne Bezug (z. B. Interessenten) werden nach dieser Zeit ohne Änderung vorgeschlagen.</summary>
    public int MonthsWithoutRelation { get; set; } = 12;
}
