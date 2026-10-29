namespace Elocaro.Domain.Common;

/// <summary>
/// Base of all persisted entities. Audit fields, deletion flag and version are set exclusively by the
/// infrastructure on save (via the EF change tracker), hence the setters are private.
/// Deletion is logical only (<see cref="Deleted"/>).
/// </summary>
public abstract class Entity
{
    /// <summary>Time-sortable GUID (v7) so that indexes do not fragment.</summary>
    public Guid Id { get; protected set; } = Guid.CreateVersion7();

    public DateTimeOffset CreatedAt { get; private set; }

    public string? CreatedBy { get; private set; }

    public DateTimeOffset? ModifiedAt { get; private set; }

    public string? ModifiedBy { get; private set; }

    public bool Deleted { get; private set; }

    /// <summary>Optimistic concurrency (mapped to the system column <c>xmin</c> in PostgreSQL).</summary>
    public uint Version { get; private set; }
}
