namespace Elocaro.Application.Audit;

/// <summary>
/// Takes back the creation of a record of a finance type by a counter entry instead of deleting it (ADR 0007, step 5). The module that owns the
/// type implements it; the undo service collects all of them. A finance type without an implementation cannot be taken back.
/// </summary>
public interface IUndoCompensation
{
    /// <summary>Name of the entity type this compensates, e.g. <c>Charge</c>.</summary>
    string EntityType { get; }

    /// <summary>Why the record cannot be compensated (German, for the user), or <c>null</c> if it can.</summary>
    Task<string?> CheckAsync(Guid entityId, CancellationToken cancellationToken = default);

    /// <summary>Adds the counter entry to the context without saving; the undo service saves it with its own operation. Returns the reason if it is not possible.</summary>
    Task<string?> CompensateAsync(Guid entityId, CancellationToken cancellationToken = default);
}
