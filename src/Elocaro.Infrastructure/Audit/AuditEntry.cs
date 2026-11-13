using Elocaro.Application.Audit;

namespace Elocaro.Infrastructure.Audit;

/// <summary>
/// An entry in the audit log. Deliberately not an <see cref="Domain.Common.Entity"/>: immutable,
/// not soft-deletable, removed only by the retention rule.
/// </summary>
public sealed class AuditEntry
{
    private AuditEntry()
    {
        User = "";
        EntityType = "";
        ChangesJson = "[]";
    }

    public AuditEntry(DateTimeOffset timestamp, string user, string entityType, Guid entityId, AuditAction action, string changesJson, string? purpose = null)
    {
        Timestamp = timestamp;
        User = user;
        EntityType = entityType;
        EntityId = entityId;
        Action = action;
        ChangesJson = changesJson;
        Purpose = purpose;
    }

    public Guid Id { get; private set; } = Guid.CreateVersion7();

    public DateTimeOffset Timestamp { get; private set; }

    public string User { get; private set; }

    public string EntityType { get; private set; }

    public Guid EntityId { get; private set; }

    public AuditAction Action { get; private set; }

    /// <summary>List of <see cref="FieldChange"/> as JSON (jsonb).</summary>
    public string ChangesJson { get; private set; }

    /// <summary>For <see cref="AuditAction.Read"/>: reason for the access.</summary>
    public string? Purpose { get; private set; }

    /// <summary>The <see cref="Operation"/> this entry belongs to (empty for entries from before operations existed).</summary>
    public Guid? OperationId { get; private set; }

    /// <summary>Typed values before a change, for taking it back (never shown; read only by the undo). Null if the type is not restorable or nothing changed.</summary>
    public string? RestoreJson { get; private set; }

    public void SetRestore(string? restoreJson) => RestoreJson = restoreJson;

    public void AssignOperation(Guid operationId) => OperationId ??= operationId;
}
