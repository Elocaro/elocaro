namespace Elocaro.Application.Audit;

/// <summary>Reads the change history of a record from the audit log.</summary>
public interface IChangeHistory
{
    /// <param name="entityType">Class name of the entity, e.g. <c>Mietvertrag</c>.</param>
    Task<IReadOnlyList<AuditEntryView>> GetAsync(string entityType, Guid entityId, CancellationToken cancellationToken = default);
}

public enum AuditAction
{
    Created = 0,
    Modified = 1,
    Deleted = 2,
    Read = 3,
}

public sealed record AuditEntryView(
    DateTimeOffset Timestamp,
    string User,
    AuditAction Action,
    IReadOnlyList<FieldChange> Changes,
    string? Purpose);

/// <param name="Old">Previous value (as text), <c>null</c> on creation.</param>
/// <param name="New">New value (as text).</param>
public sealed record FieldChange(string Field, string? Old, string? New);
