namespace Elocaro.Application.Audit;

/// <summary>The recent operations (actions that changed data), newest first, with what they touched.</summary>
public interface IActivityLog
{
    Task<IReadOnlyList<ActivityRow>> RecentAsync(ChangeChannel? channel = null, int limit = 100, CancellationToken cancellationToken = default);
}

/// <param name="Label">Tool or import name; empty for a plain save in the interface.</param>
/// <param name="Touched">What the operation changed, e.g. "2 × Lease angelegt".</param>
public sealed record ActivityRow(Guid Id, DateTimeOffset Timestamp, string User, ChangeChannel Channel, string? Label, IReadOnlyList<ActivityTouch> Touched);

public sealed record ActivityTouch(string EntityType, AuditAction Action, int Count);
