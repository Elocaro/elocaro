using MietHahn.Domain.Files;

namespace MietHahn.Application.Documents;

/// <summary>Generates PDF documents from Scriban templates in a uniform layout (letterhead, DIN 5008 address window, footer).</summary>
public interface IPdfGenerator
{
    /// <summary>Renders the document and returns the PDF bytes (PDF/A-2b).</summary>
    Task<byte[]> GenerateAsync(PdfRequest job, CancellationToken cancellationToken = default);

    /// <summary>Renders the document and stores it immutably in the file store.</summary>
    Task<StoredFile> GenerateAndStoreAsync(PdfRequest job, string fileName, CancellationToken cancellationToken = default);

    /// <summary>Renders the document as HTML exactly as it is sent to the PDF converter, without converting it (fast preview).</summary>
    Task<string> RenderHtmlAsync(PdfRequest job, CancellationToken cancellationToken = default);

    /// <summary>Joins PDFs in the given order into one.</summary>
    Task<byte[]> MergeAsync(IReadOnlyList<byte[]> documents, CancellationToken cancellationToken = default);
}

/// <param name="Template">Name of an embedded template (<c>Pdf/Templates/{name}.sbn</c>) – or <c>null</c> if <paramref name="TemplateText"/> is set.</param>
/// <param name="Title">Document title (PDF metadata and heading in the layout).</param>
/// <param name="Model">Data for the template (snake_case).</param>
/// <param name="Recipient">With a recipient a letter is produced (address window, fold marks), otherwise a plain document.</param>
/// <param name="TemplateText">Template text, e.g. from the user-maintained template management.</param>
/// <param name="Date">Letter date; defaults to today (German time).</param>
/// <param name="PropertyId">Property whose sender details (landlord profile) the document carries; empty = the standard profile.</param>
public sealed record PdfRequest(
    string? Template,
    string Title,
    object? Model = null,
    PostalAddress? Recipient = null,
    string? TemplateText = null,
    DateOnly? Date = null,
    Guid? PropertyId = null);

/// <param name="Rows">Address lines, e.g. name, street, postcode city (at most 6).</param>
/// <param name="Remark">Optional remark above the address, e.g. "Einschreiben".</param>
public sealed record PostalAddress(IReadOnlyList<string> Rows, string? Remark = null);
