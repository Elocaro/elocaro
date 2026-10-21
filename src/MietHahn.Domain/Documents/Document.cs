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
/// A stored document (file from the file store) related to a property, unit, lease and/or person.
/// It is visible internally; shares additionally make it visible to the affected tenants or assigned caretakers.
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

    /// <summary>Date of the document (e.g. invoice date), not of the upload.</summary>
    public DateOnly? Date { get; set; }

    /// <summary>Valid until (e.g. energy certificate, insurance), used for reminders.</summary>
    public DateOnly? ExpiresOn { get; set; }

    public List<string> Tags { get; private set; } = [];

    public string? Note { get; set; }

    public Guid? PropertyId { get; private set; }

    public Guid? UnitId { get; private set; }

    public Guid? LeaseId { get; private set; }

    public Guid? PersonId { get; private set; }

    /// <summary>Exact template version a generated document was created from (letters); <c>null</c> for uploaded files.</summary>
    public Guid? TemplateRevisionId { get; set; }

    /// <summary>Visible in the tenant portal to the tenants of the subject (lease, unit, property or person).</summary>
    public bool SharedWithTenants { get; set; }

    /// <summary>When the tenants were told by mail about the release; <c>null</c> if not yet (the job sends it once).</summary>
    public DateTimeOffset? TenantsNotifiedAt { get; private set; }

    public void MarkTenantsNotified(DateTimeOffset timestamp) => TenantsNotifiedAt = timestamp;

    /// <summary>Visible in the service area to caretakers assigned to the property.</summary>
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

    /// <summary>Comma-separated tags as entered: trimmed, without duplicates (case-insensitive).</summary>
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

/// <summary>A user opened or downloaded a released document: indication of receipt for the landlord.</summary>
public sealed class DocumentRead : Entity
{
    private DocumentRead()
    {
    }

    public DocumentRead(Guid documentId, Guid userId, DateTimeOffset readAt)
    {
        DocumentId = documentId;
        UserId = userId;
        ReadAt = readAt;
    }

    public Guid DocumentId { get; private set; }

    public Guid UserId { get; private set; }

    /// <summary>First time the user opened the document.</summary>
    public DateTimeOffset ReadAt { get; private set; }
}
