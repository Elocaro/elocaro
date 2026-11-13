using Elocaro.Application.Audit;

namespace Elocaro.Infrastructure.Audit;

/// <summary>One action that changed data: one save, or all saves inside an <see cref="IOperationScope"/>. Not soft-deletable, removed with the audit retention.</summary>
public sealed class Operation
{
    private Operation()
    {
        User = "";
    }

    public Operation(Guid id, DateTimeOffset timestamp, string user, ChangeChannel channel, string? label, Guid? undoOf = null)
    {
        Id = id;
        Timestamp = timestamp;
        User = user;
        Channel = channel;
        Label = label;
        UndoOf = undoOf;
    }

    public Guid Id { get; private set; }

    public DateTimeOffset Timestamp { get; private set; }

    public string User { get; private set; }

    public ChangeChannel Channel { get; private set; }

    /// <summary>Tool name, import name … (empty for a plain save in the interface).</summary>
    public string? Label { get; private set; }

    /// <summary>The operation this one took back.</summary>
    public Guid? UndoOf { get; private set; }
}
