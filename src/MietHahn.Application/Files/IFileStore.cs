using MietHahn.Domain.Files;

namespace MietHahn.Application.Files;

/// <summary>Ablage für hochgeladene Dateien (Belege, Fotos, Dokumente).</summary>
public interface IFileStore
{
    /// <summary>
    /// Prüft, bereitet auf (Fotos: drehen, verkleinern, Metadaten entfernen) und speichert die Datei.
    /// Der Eintrag wird dem DbContext hinzugefügt und gespeichert.
    /// </summary>
    /// <exception cref="FileRejectedException">Typ nicht erlaubt, zu groß oder leer.</exception>
    Task<StoredFile> SaveAsync(Stream content, string fileName, CancellationToken cancellationToken = default);

    /// <summary>Öffnet den Inhalt zum Lesen oder liefert <c>null</c>, wenn die Datei unbekannt ist.</summary>
    Task<OpenedFile?> OpenAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed record OpenedFile(StoredFile StoredFile, Stream Content) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Content.DisposeAsync();
}

/// <summary>Die Datei wurde abgelehnt; <see cref="Exception.Message"/> ist für Benutzer formuliert.</summary>
public sealed class FileRejectedException : Exception
{
    public FileRejectedException()
    {
    }

    public FileRejectedException(string message)
        : base(message)
    {
    }

    public FileRejectedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
