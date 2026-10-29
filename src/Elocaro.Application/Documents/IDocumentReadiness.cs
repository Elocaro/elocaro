using Elocaro.Domain.Documents;

namespace Elocaro.Application.Documents;

/// <summary>
/// Checks before the user starts: what must exist before a document can be created (landlord profile, template, living area).
/// Pages show the result up front instead of letting the action fail.
/// </summary>
public interface IDocumentReadiness
{
    /// <param name="kind">Kind of document to create; <c>null</c> checks only what every document needs.</param>
    /// <param name="leaseId">Lease the document is for (checks the living area of its unit).</param>
    Task<IReadOnlyList<ReadinessIssue>> CheckAsync(TemplateKind? kind = null, Guid? leaseId = null, CancellationToken cancellationToken = default);
}

public enum ReadinessIssueKind
{
    LandlordProfile,
    Template,
    LivingArea,
}

/// <param name="Blocking">The document cannot be created until this is fixed; otherwise it is a warning.</param>
/// <param name="Link">Page where it is fixed.</param>
public sealed record ReadinessIssue(ReadinessIssueKind Kind, string Message, string Link, string LinkText, bool Blocking);
