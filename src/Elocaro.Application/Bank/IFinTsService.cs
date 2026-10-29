using Elocaro.Application.Common;

namespace Elocaro.Application.Bank;

public enum FinTsPhase
{
    /// <summary>Connecting to the bank or waiting for its answer.</summary>
    Running = 0,

    /// <summary>The bank asks for a TAN (<see cref="FinTsFetchState.Message"/> holds its instructions).</summary>
    TanRequired = 1,

    /// <summary>Decoupled procedure: approve in the banking app, then confirm.</summary>
    ApprovalRequired = 2,

    /// <summary>Done; <see cref="FinTsFetchState.ImportId"/> is the preview to confirm.</summary>
    Completed = 3,

    Failed = 4,
}

public sealed record FinTsFetchState(FinTsPhase Phase, string? Message = null, Guid? ImportId = null);

/// <summary>
/// Direct retrieval of account transactions via FinTS/HBCI. The PIN only lives in memory for the duration of one retrieval.
/// The result lands in the usual import preview and is imported only after confirmation.
/// </summary>
public interface IFinTsService
{
    /// <summary>Starts a retrieval in the background; <see cref="Result.Id"/> identifies it for the other methods.</summary>
    Task<Result> StartAsync(Guid bankAccountId, string pin, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>Current state; as soon as the transactions are in, the import preview is created here (as the calling user).</summary>
    Task<FinTsFetchState?> StateAsync(Guid fetchId, CancellationToken cancellationToken = default);

    /// <summary>Hands the TAN to the waiting dialog.</summary>
    void ProvideTan(Guid fetchId, string tan);

    /// <summary>Confirms that the approval in the banking app was given (decoupled procedure).</summary>
    void ConfirmApproval(Guid fetchId);

    void Cancel(Guid fetchId);
}
