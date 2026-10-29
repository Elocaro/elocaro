using Elocaro.Domain.Common;

namespace Elocaro.Domain.Documents;

public enum TemplateKind
{
    /// <summary>Free letter to tenants (single letter or mass letter to all tenants of a property).</summary>
    Letter = 0,

    /// <summary>Reusable part (letterhead, closing, bank details) that letters include with <c>{{ include 'Name' }}</c>.</summary>
    Snippet = 1,

    /// <summary>Lease or shared-flat room contract, rendered from the data of a lease (see <c>ContractModel</c>).</summary>
    Contract = 2,

    /// <summary>Letter about a rent increase, rendered from a <c>RentIncreaseModel</c>.</summary>
    RentIncrease = 3,
}

/// <summary>
/// A user-maintained text template (Scriban). The current text lives here; every change of the text is additionally kept
/// as an immutable <see cref="DocumentTemplateRevision"/>, so that generated documents can refer to the exact version.
/// </summary>
public sealed class DocumentTemplate : Entity
{
    public const int MaxBodyLength = 100_000;

    private DocumentTemplate()
    {
        Name = "";
        Subject = "";
        Body = "";
    }

    public DocumentTemplate(string name, TemplateKind kind, string? description, string subject, string body)
    {
        Kind = kind;
        Name = "";
        Subject = "";
        Body = "";
        Edit(name, description, subject, body);
    }

    public string Name { get; private set; }

    public TemplateKind Kind { get; private set; }

    public string? Description { get; private set; }

    /// <summary>Title of the generated document (PDF title, heading); may itself contain placeholders.</summary>
    public string Subject { get; private set; }

    public string Body { get; private set; }

    /// <summary>Number of the current text version, starting at 1; raised with every change of subject or body.</summary>
    public int Revision { get; private set; }

    /// <summary>Applies the change; <c>true</c> if subject or body changed, i.e. a new revision has to be kept.</summary>
    public bool Edit(string name, string? description, string subject, string body)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new RuleViolationException("Bitte einen Namen angeben.");
        }

        subject ??= "";
        if (Kind != TemplateKind.Snippet && string.IsNullOrWhiteSpace(subject))
        {
            throw new RuleViolationException("Bitte einen Betreff angeben.");
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            throw new RuleViolationException("Der Vorlagentext darf nicht leer sein.");
        }

        if (body.Length > MaxBodyLength)
        {
            throw new RuleViolationException("Der Vorlagentext ist zu lang.");
        }

        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        var changed = Revision == 0 || subject.Trim() != Subject || body != Body;
        if (changed)
        {
            Subject = subject.Trim();
            Body = body;
            Revision++;
        }

        return changed;
    }
}

/// <summary>Immutable snapshot of a template text; never changed after creation.</summary>
public sealed class DocumentTemplateRevision : Entity
{
    private DocumentTemplateRevision()
    {
        Subject = "";
        Body = "";
    }

    public DocumentTemplateRevision(DocumentTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        TemplateId = template.Id;
        Number = template.Revision;
        Subject = template.Subject;
        Body = template.Body;
    }

    public Guid TemplateId { get; private set; }

    public int Number { get; private set; }

    public string Subject { get; private set; }

    public string Body { get; private set; }
}
