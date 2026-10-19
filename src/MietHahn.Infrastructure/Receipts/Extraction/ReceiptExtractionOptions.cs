namespace MietHahn.Infrastructure.Receipts.Extraction;

public enum ExtractionProvider
{
    /// <summary>No extraction, receipts are entered by hand (default).</summary>
    None = 0,

    /// <summary>Claude Messages API (cloud, the documents leave the server).</summary>
    Claude = 1,

    /// <summary>Ollama (local or in the own network, vision model).</summary>
    Ollama = 2,
}

/// <summary>
/// Configuration section <c>ReceiptExtraction</c>. Environment variables: <c>ReceiptExtraction__Provider</c>,
/// <c>ReceiptExtraction__Claude__ApiKey</c> and so on. The API key comes only from configuration (never from the database).
/// </summary>
public sealed class ReceiptExtractionOptions
{
    public const string Section = "ReceiptExtraction";

    public ExtractionProvider Provider { get; set; } = ExtractionProvider.None;

    /// <summary>
    /// Explicit consent that documents may be sent to an external provider (Claude). Without it, the Claude provider
    /// does nothing, even if a key is present. Not needed for Ollama on the own network.
    /// </summary>
    public bool AcceptExternalProcessing { get; set; }

    public int TimeoutSeconds { get; set; } = 120;

    /// <summary>Upper limit for all pages of one request, in MB.</summary>
    public int MaxRequestMb { get; set; } = 20;

    public ClaudeExtractionOptions Claude { get; set; } = new();

    public OllamaExtractionOptions Ollama { get; set; } = new();
}

public sealed class ClaudeExtractionOptions
{
    /// <summary>Secret, only via configuration or environment (<c>ReceiptExtraction__Claude__ApiKey</c>).</summary>
    public string? ApiKey { get; set; }

    public string Model { get; set; } = "claude-sonnet-5-5";

    public Uri BaseUrl { get; set; } = new("https://api.anthropic.com/");

    public string ApiVersion { get; set; } = "2023-06-01";

    public int MaxTokens { get; set; } = 2048;
}

public sealed class OllamaExtractionOptions
{
    public Uri BaseUrl { get; set; } = new("http://localhost:11434/");

    /// <summary>A model with image input, e.g. <c>qwen2.5vl:7b</c> or <c>llama3.2-vision</c>.</summary>
    public string Model { get; set; } = "qwen2.5vl:7b";
}
