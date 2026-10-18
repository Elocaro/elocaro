using MietHahn.Domain.Gemeinsam;

namespace MietHahn.Domain.Dateien;

/// <summary>
/// Metadaten einer abgelegten Datei. Der Inhalt liegt inhaltsadressiert (SHA-256) in der Dateiablage;
/// identische Inhalte werden nur einmal gespeichert, können aber von mehreren <see cref="Datei"/>-Einträgen referenziert werden.
/// </summary>
public sealed class Datei : Entitaet
{
    public const int MaxNameLaenge = 255;

    private Datei()
    {
        Name = "";
        MimeTyp = "";
        Sha256 = "";
    }

    public Datei(string name, string mimeTyp, long groesse, string sha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(mimeTyp);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(groesse);
        if (sha256 is not { Length: 64 } || !sha256.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("SHA-256 muss 64 Hex-Zeichen lang sein.", nameof(sha256));
        }

        Name = name.Length > MaxNameLaenge ? name[..MaxNameLaenge] : name;
        MimeTyp = mimeTyp;
        Groesse = groesse;
        Sha256 = sha256.ToLowerInvariant();
    }

    /// <summary>Ursprünglicher Dateiname (nur zur Anzeige/zum Download, nie als Pfad verwenden).</summary>
    public string Name { get; private set; }

    public string MimeTyp { get; private set; }

    /// <summary>Größe in Bytes des gespeicherten (ggf. aufbereiteten) Inhalts.</summary>
    public long Groesse { get; private set; }

    /// <summary>Hex-kodierter SHA-256 des gespeicherten Inhalts (Kleinbuchstaben).</summary>
    public string Sha256 { get; private set; }

    public bool IstBild => MimeTyp.StartsWith("image/", StringComparison.Ordinal);
}
