using System.ComponentModel.DataAnnotations;
using MietHahn.Application.Common;
using MietHahn.Domain.Documents;

namespace MietHahn.Application.Documents;

/// <summary>Management of user-maintained document templates (editor, versions, preview).</summary>
public interface IDocumentTemplates
{
    Task<IReadOnlyList<TemplateRow>> ListAsync(CancellationToken cancellationToken = default);

    Task<TemplateInput?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Creates (<paramref name="id"/> is <c>null</c>) or changes a template. Syntax and placeholders are checked first.</summary>
    Task<Result> SaveAsync(Guid? id, TemplateInput data, CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TemplateRevisionRow>> RevisionsAsync(Guid id, CancellationToken cancellationToken = default);

    Task<TemplateRevisionText?> RevisionAsync(Guid revisionId, CancellationToken cancellationToken = default);

    /// <summary>Renders the (even unsaved) text with the sample data of the catalog as PDF; stores nothing.</summary>
    Task<TemplatePreview> PreviewAsync(TemplateKind kind, string subject, string body, CancellationToken cancellationToken = default);
}

public sealed class TemplateInput
{
    [Required(ErrorMessage = "Bitte einen Namen angeben.")]
    [StringLength(100)]
    public string Name { get; set; } = "";

    public TemplateKind Kind { get; set; }

    [StringLength(500)]
    public string? Description { get; set; }

    [Required(ErrorMessage = "Bitte einen Betreff angeben.")]
    [StringLength(200)]
    public string Subject { get; set; } = "";

    [Required(ErrorMessage = "Der Vorlagentext darf nicht leer sein.")]
    [StringLength(DocumentTemplate.MaxBodyLength)]
    public string Body { get; set; } = "";

    /// <summary>Number of the current revision (read only).</summary>
    public int Revision { get; set; }
}

public sealed record TemplateRow(Guid Id, string Name, TemplateKind Kind, string? Description, int Revision, DateTimeOffset? ChangedAt);

public sealed record TemplateRevisionRow(Guid Id, int Number, DateTimeOffset Timestamp, string? CreatedBy, bool IsCurrent);

public sealed record TemplateRevisionText(Guid Id, Guid TemplateId, int Number, string Subject, string Body);

/// <summary>Either the PDF or the errors (phrased for users, e.g. syntax error or unknown placeholder).</summary>
public sealed record TemplatePreview(byte[]? Pdf, IReadOnlyList<string> Errors);

/// <summary>Placeholder of a template kind; <see cref="Example"/> is what the preview shows.</summary>
public sealed record Placeholder(string Name, string Description, string Example);

/// <summary>Data of a letter: names are the placeholders (snake_case) in the templates.</summary>
public sealed record LetterModel(
    string Salutation,
    string TenantName,
    string RentalUnit,
    string PropertyName,
    string PropertyStreet,
    string PropertyPostalCode,
    string PropertyCity,
    DateOnly LeaseStart,
    DateOnly? LeaseEnd,
    string SenderName,
    DateOnly Today);

/// <summary>Documented placeholders and sample data per template kind.</summary>
public static class TemplateCatalog
{
    public static IReadOnlyList<Placeholder> Placeholders(TemplateKind kind) => kind switch
    {
        TemplateKind.Letter =>
        [
            new("salutation", "Anrede, z. B. „Sehr geehrte Frau Muster,“", "Sehr geehrte Frau Muster,"),
            new("tenant_name", "Name des Mieters (bei mehreren: alle)", "Erika Muster"),
            new("rental_unit", "Mietsache: Einheit und Objekt", "Wohnung 1, Lindenweg 3"),
            new("property_name", "Name des Objekts", "Lindenweg 3"),
            new("property_street", "Straße des Objekts", "Lindenweg 3"),
            new("property_postal_code", "Postleitzahl des Objekts", "50667"),
            new("property_city", "Ort des Objekts", "Köln"),
            new("lease_start", "Mietbeginn (mit „date“ formatieren)", "01.04.2024"),
            new("lease_end", "Mietende, leer bei unbefristetem Vertrag", "(leer)"),
            new("sender_name", "Name des Vermieters", "Hans Vermieter"),
            new("today", "Heutiges Datum (mit „date“ formatieren)", "20.10.2026"),
        ],
        _ => [],
    };

    public static IReadOnlyList<Placeholder> Functions { get; } =
    [
        new("money amount", "Betrag als Währung", "{{ money 770 }} → 770,00 €"),
        new("date value", "Datum im Format TT.MM.JJJJ", "{{ date today }} → 20.10.2026"),
        new("number value 2", "Zahl mit Nachkommastellen", "{{ number 1234.5 2 }} → 1.234,50"),
        new("text_to_html text", "Text mit Zeilenumbrüchen", "{{ text_to_html note }}"),
    ];

    public static object Sample(TemplateKind kind) => kind switch
    {
        TemplateKind.Letter => new LetterModel("Sehr geehrte Frau Muster,", "Erika Muster", "Wohnung 1, Lindenweg 3", "Lindenweg 3", "Lindenweg 3", "50667", "Köln",
            new DateOnly(2024, 4, 1), null, "Hans Vermieter", new DateOnly(2026, 10, 20)),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
