using Elocaro.Application.Audit;

namespace Elocaro.Infrastructure.Audit;

/// <summary>The ambient operation of the current call chain (flows with async, not with the DI scope: a Blazor circuit lives for hours).</summary>
internal sealed class OperationScope : IOperationScope
{
    private static readonly AsyncLocal<State?> Current = new();

    internal static State? Active => Current.Value;

    public IDisposable Begin(ChangeChannel channel, string label)
    {
        var previous = Current.Value;
        Current.Value = new State(Guid.CreateVersion7(), channel, label);
        return new Release(previous);
    }

    internal sealed class State(Guid id, ChangeChannel channel, string label)
    {
        public Guid Id { get; } = id;

        public ChangeChannel Channel { get; } = channel;

        public string Label { get; } = label;

        /// <summary>The operation row has been written (or is part of the running save).</summary>
        public bool Recorded { get; set; }
    }

    private sealed class Release(State? previous) : IDisposable
    {
        public void Dispose() => Current.Value = previous;
    }
}
