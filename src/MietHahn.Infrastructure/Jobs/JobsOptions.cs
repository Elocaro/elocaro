namespace MietHahn.Infrastructure.Jobs;

/// <summary>Konfigurationsabschnitt <c>Jobs</c>.</summary>
public sealed class JobsOptions
{
    public const string Section = "Jobs";

    /// <summary>Scheduler starten (in Tests oder für reine Web-Instanzen abschaltbar).</summary>
    public bool Active { get; set; } = true;

    /// <summary>Wie lange Einträge im Job-Laufprotokoll aufbewahrt werden.</summary>
    public int LogRetentionDays { get; set; } = 30;
}
