namespace Elocaro.Application.Audit;

/// <summary>The recent operations (actions that changed data), newest first, with what they touched.</summary>
public interface IActivityLog
{
    Task<IReadOnlyList<ActivityRow>> RecentAsync(ChangeChannel? channel = null, int limit = 100, CancellationToken cancellationToken = default);
}

/// <param name="Label">Tool or import name; empty for a plain save in the interface.</param>
/// <param name="Touched">What the operation changed, e.g. "2 × Lease angelegt".</param>
/// <param name="TakesBack">The operation this one took back (an undo), otherwise null.</param>
/// <param name="Undone">The operation has been taken back.</param>
/// <param name="Restorable">All touched types can be taken back; whether it works now (later changes) is decided by <see cref="IOperationUndo.CheckAsync"/>.</param>
public sealed record ActivityRow(
    Guid Id,
    DateTimeOffset Timestamp,
    string User,
    ChangeChannel Channel,
    string? Label,
    IReadOnlyList<ActivityTouch> Touched,
    Guid? TakesBack = null,
    bool Undone = false,
    bool Restorable = false);

public sealed record ActivityTouch(string EntityType, AuditAction Action, int Count);
