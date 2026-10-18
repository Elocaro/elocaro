using MietHahn.Domain.Dateien;

namespace MietHahn.Application.Dateien;

/// <summary>Ablage für hochgeladene Dateien (Belege, Fotos, Dokumente).</summary>
public interface IDateiablage
{
    /// <summary>
    /// Prüft, bereitet auf (Fotos: drehen, verkleinern, Metadaten entfernen) und speichert die Datei.
    /// Der Eintrag wird dem DbContext hinzugefügt und gespeichert.
    /// </summary>
    /// <exception cref="DateiAbgelehntException">Typ nicht erlaubt, zu groß oder leer.</exception>
    Task<Datei> SpeichernAsync(Stream inhalt, string dateiname, CancellationToken cancellationToken = default);

    /// <summary>Öffnet den Inhalt zum Lesen oder liefert <c>null</c>, wenn die Datei unbekannt ist.</summary>
    Task<GeoeffneteDatei?> OeffnenAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed record GeoeffneteDatei(Datei Datei, Stream Inhalt) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Inhalt.DisposeAsync();
}

/// <summary>Die Datei wurde abgelehnt; <see cref="Exception.Message"/> ist für Benutzer formuliert.</summary>
public sealed class DateiAbgelehntException : Exception
{
    public DateiAbgelehntException()
    {
    }

    public DateiAbgelehntException(string message)
        : base(message)
    {
    }

    public DateiAbgelehntException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
