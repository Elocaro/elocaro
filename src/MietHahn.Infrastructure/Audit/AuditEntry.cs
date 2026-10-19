using MietHahn.Application.Audit;

namespace MietHahn.Infrastructure.Audit;

/// <summary>
/// Ein Eintrag im Audit-Log. Bewusst keine <see cref="Domain.Common.Entity"/>: unveränderlich,
/// nicht logisch löschbar, wird nur durch die Aufbewahrungsregel entfernt.
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

    /// <summary>Liste von <see cref="FieldChange"/> als JSON (jsonb).</summary>
    public string ChangesJson { get; private set; }

    /// <summary>Bei <see cref="AuditAction.Read"/>: Anlass des Zugriffs.</summary>
    public string? Purpose { get; private set; }
}
