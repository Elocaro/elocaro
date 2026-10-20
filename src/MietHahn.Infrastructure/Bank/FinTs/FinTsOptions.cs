namespace MietHahn.Infrastructure.Bank.FinTs;

/// <summary>
/// Configuration section <c>FinTs</c> (environment variables <c>FinTs__ProductId</c>, <c>FinTs__ProductVersion</c>).
/// The product registration number is issued by Die Deutsche Kreditwirtschaft (see docs/banking-fints.md) and belongs in the
/// configuration, never into the repository.
/// </summary>
public sealed class FinTsOptions
{
    public const string Section = "FinTs";

    public string? ProductId { get; set; }

    /// <summary>Version sent to the bank with the product number; empty = version of the application.</summary>
    public string? ProductVersion { get; set; }

    /// <summary>How long a retrieval may wait for the TAN or the approval before it is aborted.</summary>
    public int TanTimeoutSeconds { get; set; } = 300;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ProductId);
}
