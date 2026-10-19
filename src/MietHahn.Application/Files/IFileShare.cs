using System.Security.Claims;

namespace MietHahn.Application.Files;

/// <summary>
/// Extension point for file access outside the administration: business modules (e.g. documents, tickets)
/// report whether a file is shared with the user (tenants only see their documents, etc.).
/// </summary>
public interface IFileShare
{
    Task<bool> IsSharedAsync(Guid fileId, ClaimsPrincipal user, CancellationToken cancellationToken = default);
}
