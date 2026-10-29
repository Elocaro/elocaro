using System.Collections.Concurrent;
using Elocaro.Application.Bank;
using Elocaro.Domain.Bank;

namespace Elocaro.Infrastructure.Bank.FinTs;

/// <summary>One running retrieval. The PIN is dropped as soon as the dialog with the bank is over.</summary>
internal sealed class FinTsSession(Guid bankAccountId, string iban, DateTimeOffset started) : IDisposable
{
    private readonly Lock gate = new();
    private TaskCompletionSource<string>? answer;

    public Guid BankAccountId { get; } = bankAccountId;

    public string Iban { get; } = iban;

    public DateTimeOffset Started { get; } = started;

    public CancellationTokenSource Cancellation { get; } = new();

    public FinTsFetchState State { get; private set; } = new(FinTsPhase.Running);

    /// <summary>Transactions delivered by the bank, waiting for the import preview.</summary>
    public IReadOnlyList<ParsedTransaction>? Result { get; private set; }

    public bool Finished => State.Phase is FinTsPhase.Completed or FinTsPhase.Failed;

    public void Set(FinTsFetchState state)
    {
        lock (gate)
        {
            State = state;
        }
    }

    public void Succeed(IReadOnlyList<ParsedTransaction> transactions)
    {
        lock (gate)
        {
            Result = transactions;
        }
    }

    /// <summary>Waits in the dialog until the user answers (TAN or approval) or the retrieval is canceled.</summary>
    public async Task<string> AskAsync(FinTsPhase phase, string message, CancellationToken cancellationToken)
    {
        TaskCompletionSource<string> source;
        lock (gate)
        {
            source = answer = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            State = new FinTsFetchState(phase, message);
        }

        await using var registration = cancellationToken.Register(() => source.TrySetCanceled(cancellationToken));
        var reply = await source.Task;
        Set(new FinTsFetchState(FinTsPhase.Running));
        return reply;
    }

    public bool Answer(string reply)
    {
        lock (gate)
        {
            return answer?.TrySetResult(reply) == true;
        }
    }

    public void Dispose() => Cancellation.Dispose();
}

/// <summary>Running and recently finished retrievals (in memory only; lost on restart, which is fine).</summary>
internal sealed class FinTsSessions
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    private readonly ConcurrentDictionary<Guid, FinTsSession> sessions = new();

    public Guid Add(FinTsSession session, DateTimeOffset now)
    {
        foreach (var old in sessions.Where(s => s.Value.Started + Lifetime < now).ToList())
        {
            if (sessions.TryRemove(old.Key, out var removed))
            {
                removed.Cancellation.Cancel();
            }
        }

        var id = Guid.NewGuid();
        sessions[id] = session;
        return id;
    }

    public FinTsSession? Find(Guid id) => sessions.GetValueOrDefault(id);

    public bool IsRunning(Guid bankAccountId) => sessions.Values.Any(s => s.BankAccountId == bankAccountId && !s.Finished);
}
