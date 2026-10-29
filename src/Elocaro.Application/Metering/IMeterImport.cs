namespace Elocaro.Application.Metering;

/// <summary>
/// Import of meter readings from the CSV export of remote reading systems (heat cost allocators, heat and water meters).
/// The columns are found by their header (meter number, date, reading); the meter is identified by its number.
/// </summary>
public interface IMeterImport
{
    /// <summary>Shows what an import would do; nothing is saved.</summary>
    Task<ReadingImportPreview> PreviewAsync(Guid propertyId, byte[] csv, CancellationToken cancellationToken = default);

    /// <summary>Saves the new readings (rows of the preview with status <see cref="ImportRowStatus.New"/>); everything else is skipped.</summary>
    Task<ReadingImportOutcome> ImportAsync(Guid propertyId, byte[] csv, CancellationToken cancellationToken = default);
}

public enum ImportRowStatus
{
    /// <summary>Will be saved.</summary>
    New = 0,

    /// <summary>The same reading exists already (re-importing a file is harmless).</summary>
    Duplicate = 1,

    /// <summary>No meter with this number in the property.</summary>
    UnknownMeter = 2,

    /// <summary>Not plausible or not allowed (reading runs backwards, meter not installed that day, other value on that day).</summary>
    Rejected = 3,
}

/// <param name="Note">Reason for a skipped row, or a plausibility hint for a row that will be saved.</param>
public sealed record ImportRow(int Line, string MeterNumber, string? MeterName, DateOnly? Date, decimal? Value, ImportRowStatus Status, string? Note);

public sealed record ReadingImportPreview(IReadOnlyList<ImportRow> Rows, IReadOnlyList<string> Errors)
{
    public int New => Rows.Count(r => r.Status == ImportRowStatus.New);

    public bool CanImport => Errors.Count == 0 && New > 0;
}

public sealed record ReadingImportOutcome(bool Succeeded, IReadOnlyList<string> Errors, int Imported, int Skipped);
