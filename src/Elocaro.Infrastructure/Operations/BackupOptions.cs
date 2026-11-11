namespace Elocaro.Infrastructure.Operations;

/// <summary>Configuration section <c>Backup</c>.</summary>
public sealed class BackupOptions
{
    public const string Section = "Backup";

    /// <summary>Status file of the backup container (e.g. <c>/backup-status/backup.json</c>); empty = monitoring off.</summary>
    public string? StatusFile { get; set; }

    /// <summary>Address of the backup container's API (e.g. <c>http://backup:8080/</c>); empty = no control from the app.</summary>
    public string? Url { get; set; }

    /// <summary>Token for that API (<c>BACKUP_API_TOKEN</c>).</summary>
    public string? ApiToken { get; set; }

    /// <summary>A warning is issued once the last successful backup reaches this age (daily + buffer).</summary>
    public int MaxAgeHours { get; set; } = 26;
}
