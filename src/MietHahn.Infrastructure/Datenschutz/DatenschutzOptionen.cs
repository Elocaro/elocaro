namespace MietHahn.Infrastructure.Datenschutz;

/// <summary>Konfigurationsabschnitt <c>Datenschutz</c>.</summary>
public sealed class DatenschutzOptionen
{
    public const string Abschnitt = "Datenschutz";

    /// <summary>
    /// Aufbewahrung nach Ende der letzten Beziehung (z. B. Vertragsende), bevor die Anonymisierung vorgeschlagen wird.
    /// 10 Jahre entsprechen der längsten steuerlichen Frist (§ 147 AO).
    /// </summary>
    public int AufbewahrungJahre { get; set; } = 10;

    /// <summary>Personen ohne Bezug (z. B. Interessenten) werden nach dieser Zeit ohne Änderung vorgeschlagen.</summary>
    public int OhneBezugMonate { get; set; } = 12;
}
