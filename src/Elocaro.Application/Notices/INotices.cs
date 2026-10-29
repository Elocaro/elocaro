using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Elocaro.Application.Common;

namespace Elocaro.Application.Notices;

/// <summary>
/// Notices for the tenants of a property. The administration writes and archives them; tenants see the current ones
/// of the properties they live in. Opening a notice marks it as read for the account.
/// </summary>
public interface INotices
{
    /// <summary>Administration: all notices, current first.</summary>
    Task<IReadOnlyList<NoticeRow>> ListAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default);

    /// <summary>Tenant: current notices of the own properties. Others: empty.</summary>
    Task<IReadOnlyList<TenantNotice>> CurrentAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default);

    /// <summary>Administration: a notice with attachments and who has read it.</summary>
    Task<NoticeDetails?> GetAsync(ClaimsPrincipal user, Guid id, CancellationToken cancellationToken = default);

    Task<Result> CreateAsync(ClaimsPrincipal user, NoticeInput input, IReadOnlyList<Guid> fileIds, CancellationToken cancellationToken = default);

    /// <summary>Administration: ends the validity today.</summary>
    Task<Result> WithdrawAsync(ClaimsPrincipal user, Guid id, CancellationToken cancellationToken = default);

    /// <summary>Tenant: confirms that the notice has been read.</summary>
    Task<Result> AcknowledgeAsync(ClaimsPrincipal user, Guid id, CancellationToken cancellationToken = default);

    /// <summary>Administration: printable PDF for the stairwell.</summary>
    Task<byte[]?> PrintAsync(ClaimsPrincipal user, Guid id, CancellationToken cancellationToken = default);

    /// <summary>Administration: notices of a property with the read status of each tenant of a lease (archive per lease).</summary>
    Task<IReadOnlyList<LeaseNotice>> ForLeaseAsync(ClaimsPrincipal user, Guid leaseId, CancellationToken cancellationToken = default);
}

public sealed class NoticeInput : IValidatableObject
{
    [Required(ErrorMessage = "Bitte den Titel angeben.")]
    [StringLength(200)]
    public string Title { get; set; } = "";

    [Required(ErrorMessage = "Bitte den Text des Aushangs angeben.")]
    [StringLength(4000)]
    public string Body { get; set; } = "";

    [Required(ErrorMessage = "Bitte das Objekt wählen.")]
    public Guid? PropertyId { get; set; }

    /// <summary>Only for these units of the property; empty = the whole property.</summary>
    public List<Guid> UnitIds { get; set; } = [];

    public DateOnly? ValidFrom { get; set; }

    public DateOnly? ValidUntil { get; set; }

    public bool RequiresAcknowledgement { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ValidFrom is { } from && ValidUntil is { } until && until < from)
        {
            yield return new ValidationResult("Das Ende der Gültigkeit liegt vor dem Beginn.", [nameof(ValidUntil)]);
        }
    }
}

public sealed record NoticeRow(Guid Id, string Title, string Property, DateOnly ValidFrom, DateOnly? ValidUntil, bool IsCurrent, bool RequiresAcknowledgement, int Recipients, int Read, string Scope = "");

public sealed record NoticeReader(string Name, DateTimeOffset? ReadAt);

public sealed record NoticeDetails(NoticeRow Row, string Body, IReadOnlyList<Guid> FileIds, IReadOnlyList<NoticeReader> Readers);

public sealed record TenantNotice(Guid Id, string Title, string Body, string Property, DateOnly ValidFrom, DateOnly? ValidUntil, bool RequiresAcknowledgement, bool Read, IReadOnlyList<Guid> FileIds);

public sealed record LeaseNotice(Guid Id, string Title, DateOnly ValidFrom, DateOnly? ValidUntil, bool RequiresAcknowledgement, IReadOnlyList<NoticeReader> Readers);
