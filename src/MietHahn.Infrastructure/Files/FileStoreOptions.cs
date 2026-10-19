namespace MietHahn.Infrastructure.Files;

/// <summary>Konfigurationsabschnitt <c>Dateiablage</c>.</summary>
public sealed class FileStoreOptions
{
    public const string Section = "FileStore";

    /// <summary>Wurzelverzeichnis (im Container ein Volume, z. B. <c>/daten/dateien</c>).</summary>
    public string Path { get; set; } = System.IO.Path.Combine("data", "files");

    public int MaxSizeMb { get; set; } = 25;

    /// <summary>Längste Bildkante nach dem Verkleinern in Pixeln.</summary>
    public int MaxImageEdge { get; set; } = 2560;

    public int JpegQuality { get; set; } = 85;
}
