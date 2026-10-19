namespace MietHahn.Infrastructure.Files;

/// <summary>Configuration section <c>FileStore</c>.</summary>
public sealed class FileStoreOptions
{
    public const string Section = "FileStore";

    /// <summary>Root directory (a volume in the container, e.g. <c>/daten/dateien</c>).</summary>
    public string Path { get; set; } = System.IO.Path.Combine("data", "files");

    public int MaxSizeMb { get; set; } = 25;

    /// <summary>Longest image edge after downscaling, in pixels.</summary>
    public int MaxImageEdge { get; set; } = 2560;

    public int JpegQuality { get; set; } = 85;
}
