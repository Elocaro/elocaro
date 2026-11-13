using System.Formats.Tar;
using System.Text.Json;

namespace Elocaro.Backup;

/// <summary>A file that waits in the container: a backup prepared for download, or an uploaded backup that is ready to be restored.</summary>
/// <param name="Name">File name (the download name, or the name of the uploaded file).</param>
/// <param name="Size">Size in bytes (for an upload: of the extracted repository).</param>
/// <param name="Snapshot">ID of the exported backup.</param>
public sealed record TransferFile(string Name, long Size, DateTimeOffset Created, string? Snapshot = null);

/// <summary>
/// Backups that leave the container as a file (download) or enter it (upload). A backup file is a restic repository with a single
/// snapshot in a .tar: still encrypted with the password of the backup. One export and one upload at a time; they wait in
/// <see cref="BackupSettings.ExportDir"/>.
/// </summary>
public sealed class BackupTransfers(BackupSettings settings, TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>An export nobody fetched is removed after this time.</summary>
    private static readonly TimeSpan ExportLifetime = TimeSpan.FromHours(2);

    private string Root => settings.ExportDir;

    public string ExportFile => Path.Combine(Root, "export.tar");

    /// <summary>Where the copy of the backup is built before it is packed.</summary>
    public string ExportWorkDir => Path.Combine(Root, "export-work");

    /// <summary>The uploaded backup, unpacked: a restic repository.</summary>
    public string ImportDir => Path.Combine(Root, "import");

    private string ExportMeta => Path.Combine(Root, "export.json");

    private string ImportMeta => Path.Combine(Root, "import.json");

    /// <summary>The prepared download, if there is one (and not too old).</summary>
    public TransferFile? Export()
    {
        var meta = Read(ExportMeta);
        if (meta is null || !File.Exists(ExportFile))
        {
            return null;
        }

        if (clock.GetUtcNow() - meta.Created > ExportLifetime)
        {
            DiscardExport();
            return null;
        }

        return meta with { Size = new FileInfo(ExportFile).Length };
    }

    public void SetExport(string name, string snapshot)
    {
        System.IO.Directory.CreateDirectory(Root);
        Write(ExportMeta, new TransferFile(name, 0, clock.GetUtcNow(), snapshot));
    }

    public void DiscardExport()
    {
        TryDelete(ExportFile);
        TryDelete(ExportMeta);
        TryDeleteDirectory(ExportWorkDir);
    }

    /// <summary>The uploaded backup that waits for the restore, if there is one.</summary>
    public TransferFile? Upload() => System.IO.Directory.Exists(ImportDir) ? Read(ImportMeta) : null;

    /// <summary>Unpacks an uploaded backup file while it arrives (no second copy on disk). Replaces an earlier upload.</summary>
    /// <exception cref="BackupException">The content is not a backup file.</exception>
    public async Task<TransferFile> SaveUploadAsync(Stream body, string name, CancellationToken cancellationToken)
    {
        DiscardUpload();
        System.IO.Directory.CreateDirectory(ImportDir);
        try
        {
            await TarFile.ExtractToDirectoryAsync(body, ImportDir, overwriteFiles: true, cancellationToken);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or FormatException or EndOfStreamException)
        {
            DiscardUpload();
            throw new BackupException("Die Datei ist keine Sicherungsdatei von Elocaro (erwartet wird eine .tar, die über „Herunterladen“ entstanden ist).");
        }
        catch
        {
            DiscardUpload();
            throw;
        }

        var size = new DirectoryInfo(ImportDir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
        var file = new TransferFile(SafeName(name), size, clock.GetUtcNow());
        Write(ImportMeta, file);
        return file;
    }

    public void DiscardUpload()
    {
        TryDeleteDirectory(ImportDir);
        TryDelete(ImportMeta);
    }

    /// <summary>The file name of an upload, without any path.</summary>
    private static string SafeName(string name)
    {
        var plain = Path.GetFileName(name.Replace('\\', '/'));
        return string.IsNullOrWhiteSpace(plain) ? "sicherung.tar" : plain.Length > 200 ? plain[..200] : plain;
    }

    private static TransferFile? Read(string file)
    {
        try
        {
            return File.Exists(file) ? JsonSerializer.Deserialize<TransferFile>(File.ReadAllText(file), Json) : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return null;
        }
    }

    private static void Write(string file, TransferFile value) => File.WriteAllText(file, JsonSerializer.Serialize(value, Json));

    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // stays until the next attempt
        }
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (System.IO.Directory.Exists(directory))
            {
                System.IO.Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // stays until the next attempt
        }
    }
}
