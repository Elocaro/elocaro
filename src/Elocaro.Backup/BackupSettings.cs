using System.Globalization;

namespace Elocaro.Backup;

/// <summary>Settings of the container (environment variables, compatible with the former shell script).</summary>
public sealed record BackupSettings(
    string ApiToken,
    string DataPath,
    string WorkDir,
    string RestoreDir,
    string StatusFile,
    string Hostname,
    string Database,
    int AppUid,
    string BackupTime,
    RetentionPolicy Retention,
    BackupTarget? EnvironmentTarget)
{
    public const string Tag = "elocaro";

    /// <summary>
    /// Marker file next to the status file: the app manages target and schedule itself, the schedule of the environment pauses.
    /// </summary>
    public string ManagedMarker => Path.Combine(Path.GetDirectoryName(StatusFile) ?? ".", "managed-by-app");

    public static BackupSettings Load(Func<string, string?> get)
    {
        string Get(string name, string fallback) => get(name) is { Length: > 0 } value ? value : fallback;
        int GetInt(string name, int fallback) => int.TryParse(get(name), NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : fallback;

        var retention = new RetentionPolicy(GetInt("BACKUP_KEEP_DAILY", 7), GetInt("BACKUP_KEEP_WEEKLY", 4), GetInt("BACKUP_KEEP_MONTHLY", 12));
        var repository = get("RESTIC_REPOSITORY");
        var password = get("RESTIC_PASSWORD");
        // Child processes inherit the container environment (AWS_*, AZURE_*, …), so the target does not need to repeat it.
        var target = string.IsNullOrWhiteSpace(repository) ? null : new BackupTarget(repository, password ?? string.Empty, null, retention);

        return new BackupSettings(
            Get("BACKUP_API_TOKEN", string.Empty),
            Get("BACKUP_DATA_PATH", "/data"),
            Get("BACKUP_WORKDIR", "/work"),
            Get("BACKUP_RESTORE_DIR", "/restore-target"),
            Get("BACKUP_STATUS_FILE", "/status/backup.json"),
            // Fixed hostname, otherwise every new container creates its own snapshot series.
            Get("BACKUP_HOSTNAME", "elocaro"),
            Get("PGDATABASE", "elocaro"),
            GetInt("APP_UID", 1654),
            Get("BACKUP_TIME", "02:30"),
            retention,
            target);
    }
}
