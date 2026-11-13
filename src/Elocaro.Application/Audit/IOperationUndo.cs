using Elocaro.Application.Common;

namespace Elocaro.Application.Audit;

/// <summary>Takes back an operation of the activity log (ADR 0007). Undoing is itself an operation; it refuses rather than overwriting later changes.</summary>
public interface IOperationUndo
{
    /// <summary>Whether the operation can be taken back now; otherwise why not (German, for the user).</summary>
    Task<UndoCheck> CheckAsync(Guid operationId, CancellationToken cancellationToken = default);

    Task<Result> UndoAsync(Guid operationId, CancellationToken cancellationToken = default);
}

public sealed record UndoCheck(bool CanUndo, string? Reason)
{
    public static UndoCheck Yes { get; } = new(true, null);

    public static UndoCheck No(string reason) => new(false, reason);
}
