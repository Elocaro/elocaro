using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using MietHahn.Application.Common;
using MietHahn.Domain.Documents;

namespace MietHahn.Application.Documents;

/// <summary>Document library per property, unit, lease and person with shares for tenants and caretakers.</summary>
public interface IDocumentLibrary
{
    Task<IReadOnlyList<DocumentRow>> SearchAsync(DocumentQuery query, CancellationToken cancellationToken = default);

    Task<DocumentDetails?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Creates a document for an already stored file.</summary>
    Task<Result> CreateAsync(Guid fileId, DocumentInput data, CancellationToken cancellationToken = default);

    Task<Result> UpdateAsync(Guid id, DocumentInput data, CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Documents shared with tenants or caretakers that the account may see (portal, service area).</summary>
    Task<IReadOnlyList<DocumentRow>> SharedAsync(ClaimsPrincipal user, Guid? propertyId = null, CancellationToken cancellationToken = default);

    /// <summary>Selection lists for the subject.</summary>
    Task<ReferenceOptions> ReferenceOptionsAsync(CancellationToken cancellationToken = default);
}

/// <param name="Text">Searches title, tags and file name.</param>
public sealed record DocumentQuery(
    string? Text = null,
    DocumentCategory? Category = null,
    Guid? PropertyId = null,
    Guid? UnitId = null,
    Guid? LeaseId = null,
    Guid? PersonId = null);

public sealed class DocumentInput
{
    [Required(ErrorMessage = "Bitte einen Titel angeben.")]
    [StringLength(200)]
    public string Title { get; set; } = "";

    public DocumentCategory Category { get; set; } = DocumentCategory.Miscellaneous;

    public DateOnly? Date { get; set; }

    public DateOnly? ExpiresOn { get; set; }

    /// <summary>Comma-separated, e.g. "Heizung, Wartung 2026".</summary>
    [StringLength(1000)]
    public string? Tags { get; set; }

    [StringLength(2000)]
    public string? Note { get; set; }

    public Guid? PropertyId { get; set; }

    public Guid? UnitId { get; set; }

    public Guid? LeaseId { get; set; }

    public Guid? PersonId { get; set; }

    public bool SharedWithTenants { get; set; }

    public bool SharedWithCaretakers { get; set; }
}

public sealed record DocumentRow(
    Guid Id,
    Guid FileId,
    string Title,
    DocumentCategory Category,
    DateOnly? Date,
    DateOnly? ExpiresOn,
    IReadOnlyList<string> Tags,
    string Reference,
    string MimeType,
    long Size,
    bool ForTenants,
    bool ForCaretaker);

public sealed record DocumentDetails(DocumentRow Document, DocumentInput Data, string FileName, DateTimeOffset UploadedAt, string? UploadedBy);

public sealed record ReferenceOption(Guid Id, string Name);

public sealed record ReferenceOptions(
    IReadOnlyList<ReferenceOption> Properties,
    IReadOnlyList<ReferenceOption> Units,
    IReadOnlyList<ReferenceOption> Leases,
    IReadOnlyList<ReferenceOption> People);
