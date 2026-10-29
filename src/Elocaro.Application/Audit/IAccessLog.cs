namespace Elocaro.Application.Audit;

/// <summary>
/// Logs read access to personal data (GDPR evidence), e.g. when opening a tenant record.
/// Can be switched off via <c>Audit:LogAccess</c>.
/// </summary>
public interface IAccessLog
{
    Task LogAsync(string entityType, Guid entityId, string purpose, CancellationToken cancellationToken = default);
}
