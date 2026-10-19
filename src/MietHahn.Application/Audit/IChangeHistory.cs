namespace MietHahn.Application.Audit;

/// <summary>Liest die Änderungshistorie eines Datensatzes aus dem Audit-Log.</summary>
public interface IChangeHistory
{
    /// <param name="entityType">Klassenname der Entität, z. B. <c>Mietvertrag</c>.</param>
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

/// <param name="Old">Bisheriger Wert (als Text), <c>null</c> bei Neuanlage.</param>
/// <param name="New">Neuer Wert (als Text).</param>
public sealed record FieldChange(string Field, string? Old, string? New);
