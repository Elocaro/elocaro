using MietHahn.Domain.Common;

namespace MietHahn.Domain.Files;

/// <summary>
/// Metadaten einer abgelegten Datei. Der Inhalt liegt inhaltsadressiert (SHA-256) in der Dateiablage;
/// identische Inhalte werden nur einmal gespeichert, können aber von mehreren <see cref="StoredFile"/>-Einträgen referenziert werden.
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

    /// <summary>Ursprünglicher Dateiname (nur zur Anzeige/zum Download, nie als Pfad verwenden).</summary>
    public string Name { get; private set; }

    public string MimeType { get; private set; }

    /// <summary>Größe in Bytes des gespeicherten (ggf. aufbereiteten) Inhalts.</summary>
    public long Size { get; private set; }

    /// <summary>Hex-kodierter SHA-256 des gespeicherten Inhalts (Kleinbuchstaben).</summary>
    public string Sha256 { get; private set; }

    public bool IsImage => MimeType.StartsWith("image/", StringComparison.Ordinal);
}
