using Elocaro.Domain.Common;

namespace Elocaro.Domain.Operations;

/// <summary>Where the backups go. Decides how the target is built from <see cref="BackupSetting.Location"/> and the credentials.</summary>
public enum BackupProvider
{
    /// <summary>Folder in the backup container (<c>/repo</c>): on the same server, for trying out only.</summary>
    Local = 0,

    /// <summary>S3-compatible storage (AWS, Hetzner, MinIO, …).</summary>
    S3 = 1,

    /// <summary>Azure Blob Storage.</summary>
    Azure = 2,

    /// <summary>SFTP server; the SSH key lies in the backup container (<c>deploy/backup-ssh</c>).</summary>
    Sftp = 3,

    /// <summary>Google Drive through rclone.</summary>
    GoogleDrive = 4,
}

/// <summary>
/// One backup target managed in the app, with its own schedule and retention (several are possible). Without any row, the target
/// and schedule come from the environment of the backup container.
/// </summary>
/// <remarks>
/// Passwords and credentials are stored encrypted by Data Protection (the keys are in the database too, so a restore brings
/// them back; a certificate, if configured, belongs in the emergency folder). They never appear in audit log or export.
/// </remarks>
[NoExport]
public sealed class BackupSetting : Entity
{
    public const int MaxLocation = 300;
    public const int MaxName = 100;

    /// <summary>How the target is called in the app (e.g. "Google Drive", "NAS im Keller").</summary>
    public string Name { get; set; } = string.Empty;

    public BackupProvider Provider { get; set; }

    /// <summary>
    /// Where the backups go, not secret: bucket URL (S3), container (Azure), <c>user@host:/path</c> (SFTP), folder name (Google Drive),
    /// empty for local.
    /// </summary>
    public string Location { get; set; } = string.Empty;

    /// <summary>Repository password, encrypted. WITHOUT IT THERE IS NO RESTORE.</summary>
    [Confidential]
    public string ProtectedPassword { get; set; } = string.Empty;

    /// <summary>Credentials of the provider as encrypted JSON (variable name → value), see <c>BackupTargets</c>.</summary>
    [Confidential]
    public string? ProtectedCredentials { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>Local time of day of the backup.</summary>
    public TimeOnly Time { get; set; } = new(2, 30);

    /// <summary>Days between two backups: 1 = daily, 7 = weekly.</summary>
    public int IntervalDays { get; set; } = 1;

    /// <summary>Day of the week of a weekly backup; <c>null</c> for a daily one (and for weekly targets from before, which count seven days from the last backup).</summary>
    public DayOfWeek? Weekday { get; set; }

    public int KeepDaily { get; set; } = 7;

    public int KeepWeekly { get; set; } = 4;

    public int KeepMonthly { get; set; } = 12;

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Name);
        if (Name.Length > MaxName)
        {
            throw new RuleViolationException("Der Name ist zu lang.");
        }

        if (IntervalDays is < 1 or > 31)
        {
            throw new RuleViolationException("Der Abstand zwischen zwei Sicherungen muss zwischen 1 und 31 Tagen liegen.");
        }

        if (Provider != BackupProvider.Local)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(Location);
        }

        if (Location.Length > MaxLocation || Location.Contains('\n', StringComparison.Ordinal) || Location.StartsWith('-'))
        {
            throw new RuleViolationException("Das Ziel ist ungültig.");
        }

        if (KeepDaily is < 0 or > 3650 || KeepWeekly is < 0 or > 520 || KeepMonthly is < 0 or > 240 || KeepDaily + KeepWeekly + KeepMonthly == 0)
        {
            throw new RuleViolationException("Die Aufbewahrung ist ungültig: mindestens eine Frist, sinnvolle Obergrenzen.");
        }
    }
}
