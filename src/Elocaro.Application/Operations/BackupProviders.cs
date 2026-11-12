using System.Text.RegularExpressions;
using Elocaro.Domain.Operations;

namespace Elocaro.Application.Operations;

/// <summary>An input field of a backup target (credential), as the settings page shows it.</summary>
/// <param name="Key">Name under which the value is stored and handed to the backup container.</param>
/// <param name="Secret">Value is never shown again; an empty input keeps the stored one.</param>
public sealed record BackupCredentialField(string Key, string Label, bool Secret, bool Required, string? Hint = null);

/// <summary>What the backup container needs: restic repository, its password and the backend credentials.</summary>
public sealed record BackupRepositoryTarget(string Repository, string Password, IReadOnlyDictionary<string, string> Environment, BackupRetention Retention);

public sealed record BackupRetention(int Daily, int Weekly, int Monthly);

/// <summary>Knows the providers: which fields they need and how the restic target is built from them.</summary>
public static partial class BackupProviders
{
    /// <summary>Name of the rclone remote; its settings reach rclone as <c>RCLONE_CONFIG_GDRIVE_*</c> variables (no config file, nothing on disk).</summary>
    private const string DriveRemote = "gdrive";

    public const string DriveTokenKey = "RCLONE_CONFIG_GDRIVE_TOKEN";
    public const string DriveClientIdKey = "RCLONE_CONFIG_GDRIVE_CLIENT_ID";
    public const string DriveClientSecretKey = "RCLONE_CONFIG_GDRIVE_CLIENT_SECRET";

    /// <summary>Folder that the app creates in Google Drive unless told otherwise.</summary>
    public const string DefaultDriveFolder = "Elocaro-Sicherung";

    [GeneratedRegex(@"^[\p{L}\p{N} _./()-]{1,200}$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex FolderName();

    public static IReadOnlyList<BackupProvider> All { get; } = [BackupProvider.Local, BackupProvider.S3, BackupProvider.Azure, BackupProvider.Sftp, BackupProvider.GoogleDrive];

    public static string Label(BackupProvider provider) => provider switch
    {
        BackupProvider.Local => "Dieser Server (nur zum Ausprobieren)",
        BackupProvider.S3 => "S3-Speicher (AWS, Hetzner, MinIO …)",
        BackupProvider.Azure => "Azure Blob Storage",
        BackupProvider.Sftp => "SFTP-Server",
        BackupProvider.GoogleDrive => "Google Drive",
        _ => provider.ToString(),
    };

    public static string LocationLabel(BackupProvider provider) => provider switch
    {
        BackupProvider.S3 => "Adresse mit Bucket und Ordner",
        BackupProvider.Azure => "Container",
        BackupProvider.Sftp => "Benutzer, Server und Ordner",
        BackupProvider.GoogleDrive => "Ordner in Google Drive",
        _ => "Ziel",
    };

    public static string? LocationHint(BackupProvider provider) => provider switch
    {
        BackupProvider.S3 => "z. B. https://s3.eu-central-1.amazonaws.com/mein-bucket/elocaro",
        BackupProvider.Azure => "Name des Containers, z. B. elocaro-backup",
        BackupProvider.Sftp => "z. B. backup@nas.example.de:/volume1/elocaro. Den SSH-Schlüssel legen Sie in deploy/backup-ssh ab.",
        BackupProvider.GoogleDrive => "Der Ordner wird angelegt, falls er fehlt. Elocaro sieht nur Dateien, die es dort selbst angelegt hat.",
        _ => null,
    };

    public static IReadOnlyList<BackupCredentialField> Fields(BackupProvider provider) => provider switch
    {
        BackupProvider.S3 =>
        [
            new("AWS_ACCESS_KEY_ID", "Zugangsschlüssel (Access Key ID)", Secret: false, Required: true),
            new("AWS_SECRET_ACCESS_KEY", "Geheimer Schlüssel (Secret Access Key)", Secret: true, Required: true),
            new("AWS_DEFAULT_REGION", "Region", Secret: false, Required: false, "z. B. eu-central-1"),
        ],
        BackupProvider.Azure =>
        [
            new("AZURE_ACCOUNT_NAME", "Name des Speicherkontos", Secret: false, Required: true),
            new("AZURE_ACCOUNT_KEY", "Zugriffsschlüssel", Secret: true, Required: true),
        ],
        BackupProvider.GoogleDrive =>
        [
            new(DriveTokenKey, "Anmeldung bei Google", Secret: true, Required: true, "Entsteht durch „Mit Google verbinden“; alternativ die Ausgabe von „rclone authorize drive“ (JSON)"),
            new(DriveClientIdKey, "Client-ID", Secret: false, Required: false, "Gehört zur Anmeldung; nur bei einer selbst angelegten Anmeldung eintragen"),
            new(DriveClientSecretKey, "Client-Geheimnis", Secret: true, Required: false),
        ],
        _ => [],
    };

    /// <summary>Messages for everything that is missing or invalid; empty if the target can be built.</summary>
    public static IReadOnlyList<string> Validate(BackupProvider provider, string location, IReadOnlyDictionary<string, string> credentials, string? password)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        var errors = new List<string>();
        if (string.IsNullOrEmpty(password))
        {
            errors.Add("Das Passwort der Sicherung fehlt.");
        }

        if (provider != BackupProvider.Local && string.IsNullOrWhiteSpace(location))
        {
            errors.Add($"{LocationLabel(provider)} fehlt.");
        }

        if (provider == BackupProvider.GoogleDrive && !FolderName().IsMatch(location ?? string.Empty))
        {
            errors.Add("Der Ordnername darf nur Buchstaben, Ziffern, Leerzeichen und _ . / ( ) - enthalten.");
        }

        errors.AddRange(Fields(provider)
            .Where(field => field.Required && string.IsNullOrWhiteSpace(credentials.GetValueOrDefault(field.Key)))
            .Select(field => $"{field.Label} fehlt."));
        return errors;
    }

    /// <summary>
    /// Provider and location of a restic repository address, the reverse of <see cref="Build"/> (for taking over the target of the
    /// server configuration). <c>null</c> for an address that the app has no provider for.
    /// </summary>
    public static (BackupProvider Provider, string Location)? Parse(string repository)
    {
        if (string.IsNullOrWhiteSpace(repository))
        {
            return null;
        }

        var value = repository.Trim();
        if (value.StartsWith("s3:", StringComparison.Ordinal))
        {
            return (BackupProvider.S3, value["s3:".Length..]);
        }

        if (value.StartsWith("azure:", StringComparison.Ordinal))
        {
            var container = value["azure:".Length..].Split(':')[0];
            return container.Length == 0 ? null : (BackupProvider.Azure, container);
        }

        if (value.StartsWith("sftp:", StringComparison.Ordinal))
        {
            return (BackupProvider.Sftp, value["sftp:".Length..]);
        }

        if (value.StartsWith($"rclone:{DriveRemote}:", StringComparison.Ordinal))
        {
            return (BackupProvider.GoogleDrive, value[$"rclone:{DriveRemote}:".Length..]);
        }

        return value.StartsWith('/') ? (BackupProvider.Local, string.Empty) : null;
    }

    /// <summary>Builds the target for the backup container. Call <see cref="Validate"/> first.</summary>
    public static BackupRepositoryTarget Build(BackupProvider provider, string location, IReadOnlyDictionary<string, string> credentials, string password, BackupRetention retention)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        var environment = Fields(provider)
            .Where(field => !string.IsNullOrWhiteSpace(credentials.GetValueOrDefault(field.Key)))
            .ToDictionary(field => field.Key, field => credentials[field.Key].Trim(), StringComparer.Ordinal);

        var repository = provider switch
        {
            BackupProvider.S3 => "s3:" + location.Trim(),
            BackupProvider.Azure => $"azure:{location.Trim()}:/",
            BackupProvider.Sftp => "sftp:" + location.Trim(),
            BackupProvider.GoogleDrive => $"rclone:{DriveRemote}:{location.Trim()}",
            _ => "/repo",
        };

        if (provider == BackupProvider.GoogleDrive)
        {
            environment["RCLONE_CONFIG_GDRIVE_TYPE"] = "drive";
            // Least privilege: the app only sees files it created itself.
            environment["RCLONE_CONFIG_GDRIVE_SCOPE"] = "drive.file";
        }

        return new BackupRepositoryTarget(repository, password, environment, retention);
    }
}
