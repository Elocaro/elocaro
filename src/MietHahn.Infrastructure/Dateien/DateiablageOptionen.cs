namespace MietHahn.Infrastructure.Dateien;

/// <summary>Konfigurationsabschnitt <c>Dateiablage</c>.</summary>
public sealed class DateiablageOptionen
{
    public const string Abschnitt = "Dateiablage";

    /// <summary>Wurzelverzeichnis (im Container ein Volume, z. B. <c>/daten/dateien</c>).</summary>
    public string Pfad { get; set; } = Path.Combine("daten", "dateien");

    public int MaxGroesseMb { get; set; } = 25;

    /// <summary>Längste Bildkante nach dem Verkleinern in Pixeln.</summary>
    public int MaxBildKante { get; set; } = 2560;

    public int JpegQualitaet { get; set; } = 85;
}
