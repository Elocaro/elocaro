namespace MietHahn.Infrastructure.Bank.FinTs;

/// <summary>
/// Configuration section <c>FinTs</c> (environment variables <c>FinTs__ProductId</c>, <c>FinTs__ProductVersion</c>).
/// The product registration number is issued by Die Deutsche Kreditwirtschaft (see docs/banking-fints.md). It identifies the
/// software, not a user or an account, so the registration of MietHahn ships as default; a fork with its own registration
/// overrides it.
/// </summary>
public sealed class FinTsOptions
{
    public const string Section = "FinTs";

    /// <summary>Product registration number of MietHahn.</summary>
    public const string DefaultProductId = "34255DF7B6C1A24BFCE7C3CD0";

    /// <summary>Overrides <see cref="DefaultProductId"/>; empty = default.</summary>
    public string? ProductId { get; set; }

    /// <summary>Version sent to the bank with the product number; empty = version of the application.</summary>
    public string? ProductVersion { get; set; }

    /// <summary>How long a retrieval may wait for the TAN or the approval before it is aborted.</summary>
    public int TanTimeoutSeconds { get; set; } = 300;

    public string EffectiveProductId => string.IsNullOrWhiteSpace(ProductId) ? DefaultProductId : ProductId.Trim();
}
