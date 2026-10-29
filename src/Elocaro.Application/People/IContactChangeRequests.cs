using Elocaro.Application.Common;
using Elocaro.Domain.People;

namespace Elocaro.Application.People;

/// <summary>Administrator's view of the contact data changes requested by tenants in the portal.</summary>
public interface IContactChangeRequests
{
    Task<IReadOnlyList<ContactChangeView>> OpenAsync(CancellationToken cancellationToken = default);

    /// <summary>Applies the requested values to the person.</summary>
    Task<Result> ApproveAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result> RejectAsync(Guid id, string? note, CancellationToken cancellationToken = default);
}

public sealed record ContactChangeView(
    Guid Id,
    Guid PersonId,
    string Person,
    DateTimeOffset RequestedAt,
    string? CurrentEmail,
    string? CurrentPhone,
    string? CurrentMobile,
    string? Email,
    string? Phone,
    string? Mobile);
