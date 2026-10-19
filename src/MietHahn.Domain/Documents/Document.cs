using MietHahn.Domain.Common;

namespace MietHahn.Domain.Documents;

public enum DocumentCategory
{
    Lease = 0,
    Minutes = 1,
    Statement = 2,
    Correspondence = 3,
    Invoice = 4,
    Insurance = 5,
    EnergyCertificate = 6,
    FloorPlan = 7,
    Photo = 8,
    HouseRules = 9,
    Miscellaneous = 99,
}

/// <summary>
/// Ein abgelegtes Dokument (Datei aus der Dateiablage) mit Bezug auf Objekt, Einheit, Vertrag und/oder Person.
/// Sichtbar ist es intern; Freigaben machen es zusätzlich für die betroffenen Mieter bzw. zugeordneten Hausmeister sichtbar.
/// </summary>
public sealed class Document : Entity
{
    public const int MaxTags = 20;

    private Document()
    {
        Title = "";
    }

    public Document(Guid fileId, string title, DocumentCategory category)
    {
        FileId = fileId;
        Title = title;
        Category = category;
        Validate();
    }

    public Guid FileId { get; private set; }

    public string Title { get; set; }

    public DocumentCategory Category { get; set; }

    /// <summary>Datum des Dokuments (z. B. Rechnungsdatum), nicht des Uploads.</summary>
    public DateOnly? Date { get; set; }

    /// <summary>Gültig bis (z. B. Energieausweis, Versicherung) für Erinnerungen.</summary>
    public DateOnly? ExpiresOn { get; set; }

    public List<string> Tags { get; private set; } = [];

    public string? Note { get; set; }

    public Guid? PropertyId { get; private set; }

    public Guid? UnitId { get; private set; }

    public Guid? LeaseId { get; private set; }

    public Guid? PersonId { get; private set; }

    /// <summary>Im Mieterportal sichtbar für die Mieter des Bezugs (Vertrag, Einheit, Objekt oder Person).</summary>
    public bool SharedWithTenants { get; set; }

    /// <summary>Im Servicebereich sichtbar für Hausmeister, denen das Objekt zugeordnet ist.</summary>
    public bool SharedWithCaretakers { get; set; }

    public void SetReference(Guid? propertyId, Guid? unitId, Guid? leaseId, Guid? personId)
    {
        if (propertyId is null && unitId is null && leaseId is null && personId is null)
        {
            throw new RuleViolationException("Bitte mindestens einen Bezug wählen (Objekt, Einheit, Vertrag oder Person).");
        }

        PropertyId = propertyId;
        UnitId = unitId;
        LeaseId = leaseId;
        PersonId = personId;
    }

    /// <summary>Kommagetrennt eingegebene Schlagworte: getrimmt, ohne Doppelte (Groß/Klein egal).</summary>
    public void SetTags(IEnumerable<string> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);
        var cleaned = tags.Select(t => t.Trim()).Where(t => t.Length > 0).DistinctBy(t => t.ToLowerInvariant()).ToList();
        if (cleaned.Count > MaxTags || cleaned.Any(t => t.Length > 50))
        {
            throw new RuleViolationException($"Höchstens {MaxTags} Schlagworte mit je bis zu 50 Zeichen.");
        }

        Tags = cleaned;
    }

    public void Validate() => ArgumentException.ThrowIfNullOrWhiteSpace(Title);
}
