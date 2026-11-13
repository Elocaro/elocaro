namespace Elocaro.Application.Audit;

/// <summary>Where a change came from.</summary>
public enum ChangeChannel
{
    /// <summary>A person in the web interface.</summary>
    Ui = 0,

    /// <summary>An AI assistant through the MCP interface.</summary>
    Mcp = 1,

    /// <summary>A background job.</summary>
    Job = 2,

    /// <summary>A data import.</summary>
    Import = 3,
}

/// <summary>
/// Groups all saves of one action (an MCP call, an import) into one operation of the audit log. Without a scope every save is its own operation
/// (channel <see cref="ChangeChannel.Ui"/>, or <see cref="ChangeChannel.Job"/> without a signed-in user).
/// </summary>
public interface IOperationScope
{
    /// <summary>Starts the scope; disposing ends it. Scopes nest, the inner one wins.</summary>
    /// <param name="undoOf">The operation this one takes back (set only by the undo).</param>
    IDisposable Begin(ChangeChannel channel, string label, Guid? undoOf = null);
}
