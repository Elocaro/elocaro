namespace Elocaro.Application.Accounts;

/// <summary>
/// Configuration section <c>Mcp</c> (environment variables <c>Mcp__Enabled</c> and so on). The MCP interface is off by default;
/// it is never available on a demo instance.
/// </summary>
public sealed class McpOptions
{
    public const string Section = "Mcp";

    public bool Enabled { get; set; }

    /// <summary>Requests per minute and client address on the MCP endpoint.</summary>
    public int RequestsPerMinute { get; set; } = 120;

    /// <summary>Most entries a tool returns in one call.</summary>
    public int MaxResults { get; set; } = 50;
}
