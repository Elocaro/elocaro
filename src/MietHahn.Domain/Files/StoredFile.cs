using MietHahn.Domain.Common;

namespace MietHahn.Domain.Files;

/// <summary>
/// Metadata of a stored file. The content is stored content-addressed (SHA-256) in the file store;
/// identical contents are stored only once but can be referenced by several <see cref="StoredFile"/> entries.
/// </summary>
public sealed class StoredFile : Entity
{
    public const int MaxNameLength = 255;

    private StoredFile()
    {
        Name = "";
        MimeType = "";
        Sha256 = "";
    }

    public StoredFile(string name, string mimeType, long size, string sha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
        if (sha256 is not { Length: 64 } || !sha256.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("SHA-256 muss 64 Hex-Zeichen lang sein.", nameof(sha256));
        }

        Name = name.Length > MaxNameLength ? name[..MaxNameLength] : name;
        MimeType = mimeType;
        Size = size;
        Sha256 = sha256.ToLowerInvariant();
    }

    /// <summary>Original file name (for display/download only, never use as a path).</summary>
    public string Name { get; private set; }

    public string MimeType { get; private set; }

    /// <summary>Size in bytes of the stored (possibly processed) content.</summary>
    public long Size { get; private set; }

    /// <summary>Hex-encoded SHA-256 of the stored content (lowercase).</summary>
    public string Sha256 { get; private set; }

    public bool IsImage => MimeType.StartsWith("image/", StringComparison.Ordinal);
}
