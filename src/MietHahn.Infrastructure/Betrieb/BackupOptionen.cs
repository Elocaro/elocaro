namespace MietHahn.Infrastructure.Betrieb;

/// <summary>Konfigurationsabschnitt <c>Backup</c>.</summary>
public sealed class BackupOptionen
{
    public const string Abschnitt = "Backup";

    /// <summary>Statusdatei des Backup-Containers (z. B. <c>/backup-status/backup.json</c>); leer = Überwachung aus.</summary>
    public string? StatusDatei { get; set; }

    /// <summary>Ab diesem Alter der letzten erfolgreichen Sicherung wird gewarnt (täglich + Puffer).</summary>
    public int MaxAlterStunden { get; set; } = 26;
}
