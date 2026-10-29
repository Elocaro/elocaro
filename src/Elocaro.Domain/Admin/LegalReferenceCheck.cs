using Elocaro.Domain.Common;

namespace Elocaro.Domain.Admin;

/// <summary>
/// Note that someone compared a legal value (Basiszinssatz, VPI, CO₂ levels, degree days) with its official source: who, when,
/// against what. The <see cref="Fingerprint"/> is the value that was checked; if the value changes, the note no longer counts.
/// </summary>
public sealed class LegalReferenceCheck : Entity
{
    private LegalReferenceCheck()
    {
        Key = "";
        Fingerprint = "";
        CheckedBy = "";
    }

    public LegalReferenceCheck(string key, string fingerprint, DateOnly checkedOn, string checkedBy, string? source)
    {
        Key = key;
        Fingerprint = fingerprint;
        CheckedOn = checkedOn;
        CheckedBy = checkedBy;
        Source = string.IsNullOrWhiteSpace(source) ? null : source.Trim();
    }

    /// <summary>Stable name of the reference, e.g. <c>carbon</c>.</summary>
    public string Key { get; private set; }

    public string Fingerprint { get; private set; }

    public DateOnly CheckedOn { get; private set; }

    public string CheckedBy { get; private set; }

    /// <summary>What it was compared with, e.g. "Gesetzestext, Anlage zu § 1".</summary>
    public string? Source { get; private set; }

    public void Renew(string fingerprint, DateOnly checkedOn, string checkedBy, string? source)
    {
        Fingerprint = fingerprint;
        CheckedOn = checkedOn;
        CheckedBy = checkedBy;
        Source = string.IsNullOrWhiteSpace(source) ? null : source.Trim();
    }
}
