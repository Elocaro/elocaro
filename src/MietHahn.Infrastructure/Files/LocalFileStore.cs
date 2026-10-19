using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MietHahn.Application.Files;
using MietHahn.Domain.Files;
using MietHahn.Infrastructure.Persistence;

namespace MietHahn.Infrastructure.Files;

/// <summary>
/// Inhaltsadressierte Ablage im Dateisystem: <c>{Pfad}/{sha[0..2]}/{sha}</c>. Identische Inhalte werden einmal
/// gespeichert. Geschrieben wird atomar (temporäre Datei + Umbenennen).
/// </summary>
internal sealed class LocalFileStore(MietHahnDbContext db, IOptions<FileStoreOptions> options) : IFileStore
{
    public async Task<StoredFile> SaveAsync(Stream content, string fileName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var o = options.Value;

        var bytes = await ReadWithLimitAsync(content, (long)o.MaxSizeMb * 1024 * 1024, cancellationToken);
        var type = FileTypeDetection.Detect(bytes.AsSpan(0, Math.Min(bytes.Length, FileTypeDetection.RequiredBytes)))
            ?? throw new FileRejectedException("Dieser Dateityp ist nicht erlaubt. Erlaubt sind PDF, JPG, PNG und HEIC.");

        if (type.IsImage)
        {
            bytes = ImageProcessing.Prepare(bytes, type.MimeType, o.MaxImageEdge, o.JpegQuality) ?? bytes;
        }

        var sha = Convert.ToHexStringLower(SHA256.HashData(bytes));
        await StoreAsync(sha, bytes, cancellationToken);

        var file = new StoredFile(SanitizeFileName(fileName, type.Extension), type.MimeType, bytes.LongLength, sha);
        db.Add(file);
        await db.SaveChangesAsync(cancellationToken);
        return file;
    }

    public async Task<OpenedFile?> OpenAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var file = await db.Set<StoredFile>().AsNoTracking().SingleOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (file is null)
        {
            return null;
        }

        var path = StoragePath(file.Sha256);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Inhalt der Datei {id} fehlt in der Ablage.", path);
        }

        return new OpenedFile(file, new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true));
    }

    internal string StoragePath(string sha256) => StoragePath(options.Value, sha256);

    internal static string StoragePath(FileStoreOptions options, string sha256) => Path.Combine(Path.GetFullPath(options.Path), sha256[..2], sha256);

    private async Task StoreAsync(string sha, byte[] bytes, CancellationToken cancellationToken)
    {
        var destination = StoragePath(sha);
        if (File.Exists(destination))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temp = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllBytesAsync(temp, bytes, cancellationToken);
        try
        {
            File.Move(temp, destination, overwrite: false);
        }
        catch (IOException) when (File.Exists(destination))
        {
            // Paralleler Upload desselben Inhalts: bereits vorhanden, gleicher Inhalt.
            File.Delete(temp);
        }
    }

    private static async Task<byte[]> ReadWithLimitAsync(Stream content, long limit, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var block = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(block, cancellationToken)) > 0)
        {
            if (buffer.Length + read > limit)
            {
                throw new FileRejectedException($"Die Datei ist zu groß (maximal {limit / 1024 / 1024} MB).");
            }

            buffer.Write(block, 0, read);
        }

        if (buffer.Length == 0)
        {
            throw new FileRejectedException("Die Datei ist leer.");
        }

        return buffer.ToArray();
    }

    /// <summary>Nur der Dateiname (keine Pfadanteile), Steuerzeichen entfernt, Endung passend zum erkannten Typ.</summary>
    internal static string SanitizeFileName(string? fileName, string extension)
    {
        var name = Path.GetFileName((fileName ?? "").Replace('\\', '/'));
        name = new string(name.Where(z => !char.IsControl(z) && !Path.GetInvalidFileNameChars().Contains(z)).ToArray()).Trim();
        var baseValue = Path.GetFileNameWithoutExtension(name);
        if (string.IsNullOrWhiteSpace(baseValue))
        {
            baseValue = "datei";
        }

        // HEIC wird unverändert, JPG/PNG neu kodiert: die Endung folgt immer dem tatsächlichen Inhalt.
        return baseValue + extension;
    }
}
