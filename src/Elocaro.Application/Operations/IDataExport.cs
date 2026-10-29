namespace Elocaro.Application.Operations;

/// <summary>
/// Full export of all data as ZIP (JSON and CSV per table, plus all files), readable without Elocaro.
/// Secrets (password hashes, tokens, keys) are excluded.
/// </summary>
public interface IDataExport
{
    /// <summary>Suggested file name, e.g. <c>elocaro-export-2026-10-18.zip</c>.</summary>
    string FileName();

    /// <summary>Writes the ZIP to <paramref name="destination"/>, async only (suitable for the response body).</summary>
    Task ExportAsync(Stream destination, CancellationToken cancellationToken = default);
}
