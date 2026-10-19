namespace MietHahn.Infrastructure.Operations;

/// <summary>Konfigurationsabschnitt <c>Backup</c>.</summary>
public sealed class BackupOptions
{
    public const string Section = "Backup";

    /// <summary>Statusdatei des Backup-Containers (z. B. <c>/backup-status/backup.json</c>); leer = Überwachung aus.</summary>
    public string? StatusFile { get; set; }

    /// <summary>Ab diesem Alter der letzten erfolgreichen Sicherung wird gewarnt (täglich + Puffer).</summary>
    public int MaxAgeHours { get; set; } = 26;
}
