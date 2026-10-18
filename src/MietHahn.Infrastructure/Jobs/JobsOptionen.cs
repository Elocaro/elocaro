namespace MietHahn.Infrastructure.Jobs;

/// <summary>Konfigurationsabschnitt <c>Jobs</c>.</summary>
public sealed class JobsOptionen
{
    public const string Abschnitt = "Jobs";

    /// <summary>Scheduler starten (in Tests oder für reine Web-Instanzen abschaltbar).</summary>
    public bool Aktiv { get; set; } = true;

    /// <summary>Wie lange Einträge im Job-Laufprotokoll aufbewahrt werden.</summary>
    public int ProtokollAufbewahrungTage { get; set; } = 30;
}
