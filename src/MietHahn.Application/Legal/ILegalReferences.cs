namespace MietHahn.Application.Legal;

/// <summary>
/// The values MietHahn takes from law and statistics (Basiszinssatz, Verbraucherpreisindex, CO₂-Stufen, Gradtagszahlen), each with its
/// date, source and whether it is up to date. A statement or letter is only as reliable as these values.
/// </summary>
public interface ILegalReferences
{
    /// <summary>The references that matter for the modules and options in use.</summary>
    Task<IReadOnlyList<LegalReference>> OverviewAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Notes that the current value was compared with its source (who, when, against what). A checked table counts as current until
    /// it changes or a year has passed.
    /// </summary>
    Task<Common.Result> MarkCheckedAsync(string key, string? source, CancellationToken cancellationToken = default);
}

public enum LegalReferenceStatus
{
    /// <summary>Maintained and current (or a checked table).</summary>
    Current,

    /// <summary>Present but outdated or not checked against an official source; needs a look.</summary>
    Check,

    /// <summary>Needed but not entered.</summary>
    Missing,
}

/// <param name="Key">Stable name, e.g. <c>base-rate</c>.</param>
/// <param name="Value">What is stored, e.g. "3,62 % p. a."; <c>null</c> when nothing is stored.</param>
/// <param name="AsOf">Valid since / newest entry, as text.</param>
/// <param name="Source">Where the value comes from and where to look it up.</param>
/// <param name="NextCheck">When the value is due to change or be looked at again.</param>
/// <param name="Link">Page where it is maintained.</param>
/// <param name="Note">What to do when the status is not <see cref="LegalReferenceStatus.Current"/>.</param>
public sealed record LegalReference(
    string Key,
    string Title,
    string? Value,
    string? AsOf,
    string Source,
    LegalReferenceStatus Status,
    DateOnly? NextCheck,
    string Link,
    string? Note,
    string? CheckedBy = null,
    DateOnly? CheckedOn = null,
    string? CheckSource = null);
