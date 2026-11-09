namespace Elocaro.Application.Accounts;

/// <summary>
/// Configuration section <c>AccessTokens</c> (environment variable <c>AccessTokens__MaxDays</c>): applies to personal access
/// tokens whichever integration uses them (currently the MCP interface, see <see cref="McpOptions"/>).
/// </summary>
public sealed class AccessTokenOptions
{
    public const string Section = "AccessTokens";

    /// <summary>Longest validity of a token in days.</summary>
    public int MaxDays { get; set; } = 90;
}
