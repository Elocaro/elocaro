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
/// Settings of the backup managed in the app (there is exactly one). Without a row, the target and schedule come from the
/// environment of the backup container.
/// </summary>
/// <remarks>
/// Passwords and credentials are stored encrypted by Data Protection (the keys are in the database too, so a restore brings
/// them back; a certificate, if configured, belongs in the emergency folder). They never appear in audit log or export.
/// </remarks>
[NoExport]
public sealed class BackupSetting : Entity
{
    public const int MaxLocation = 300;

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

    /// <summary>Local time of the daily backup.</summary>
    public TimeOnly Time { get; set; } = new(2, 30);

    public int KeepDaily { get; set; } = 7;

    public int KeepWeekly { get; set; } = 4;

    public int KeepMonthly { get; set; } = 12;

    public void Validate()
    {
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
