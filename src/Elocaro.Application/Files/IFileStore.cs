using Elocaro.Domain.Files;

namespace Elocaro.Application.Files;

/// <summary>Store for uploaded files (receipts, photos, documents).</summary>
public interface IFileStore
{
    /// <summary>
    /// Validates, processes (photos: rotate, shrink, strip metadata) and stores the file.
    /// The entry is added to the DbContext and saved.
    /// </summary>
    /// <exception cref="FileRejectedException">Type not allowed, too large or empty.</exception>
    Task<StoredFile> SaveAsync(Stream content, string fileName, CancellationToken cancellationToken = default);

    /// <summary>Opens the content for reading, or returns <c>null</c> if the file is unknown.</summary>
    Task<OpenedFile?> OpenAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed record OpenedFile(StoredFile StoredFile, Stream Content) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Content.DisposeAsync();
}

/// <summary>The file was rejected; <see cref="Exception.Message"/> is phrased for users.</summary>
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
