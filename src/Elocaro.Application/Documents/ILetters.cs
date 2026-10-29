using System.ComponentModel.DataAnnotations;

namespace Elocaro.Application.Documents;

/// <summary>Letters from a template to the tenants of a property (single letter or mass letter).</summary>
public interface ILetters
{
    /// <summary>Current leases of the property with their tenants.</summary>
    Task<IReadOnlyList<LetterRecipient>> RecipientsAsync(Guid propertyId, CancellationToken cancellationToken = default);

    /// <summary>The letter for one lease as PDF with its real data; stores nothing.</summary>
    Task<TemplatePreview> PreviewAsync(Guid templateId, Guid leaseId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates one PDF per lease and files it as document (with the template version); optionally also sends it by email.
    /// Each letter stands on its own: a failure for one lease does not stop the others.
    /// </summary>
    Task<LetterRunResult> CreateAsync(LetterRequest request, CancellationToken cancellationToken = default);
}

public sealed class LetterRequest
{
    [Required]
    public Guid TemplateId { get; set; }

    [Required]
    public Guid PropertyId { get; set; }

    public List<Guid> LeaseIds { get; set; } = [];

    /// <summary>Also send the PDF to the email addresses of the tenants (where present).</summary>
    public bool SendByMail { get; set; }

    /// <summary>Make the document visible in the tenant portal.</summary>
    public bool ShareWithTenants { get; set; }
}

public sealed record LetterRecipient(Guid LeaseId, string Tenants, string RentalUnit, bool HasEmail);

/// <param name="Mailed">Queued for sending by email.</param>
public sealed record LetterOutcome(Guid LeaseId, string Recipient, bool Succeeded, string? Message, Guid? DocumentId, bool Mailed);

public sealed record LetterRunResult(IReadOnlyList<LetterOutcome> Outcomes, IReadOnlyList<string> Errors)
{
    public int Created => Outcomes.Count(o => o.Succeeded);

    public int Mailed => Outcomes.Count(o => o.Mailed);
}
