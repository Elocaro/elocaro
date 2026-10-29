using Elocaro.Domain.Receipts;

namespace Elocaro.Application.Receipts;

/// <summary>
/// Reads receipt data from the document pages (invoice as PDF or photos). The provider is chosen by configuration
/// (<c>ReceiptExtraction:Provider</c>: None, Claude, Ollama). The result is only a suggestion.
/// </summary>
public interface IReceiptExtractor
{
    /// <summary>Which provider is active and whether it is ready; free of I/O.</summary>
    ExtractorInfo Info { get; }

    /// <exception cref="ExtractionException">Not ready, unsupported document or the provider failed; the message is phrased for users.</exception>
    Task<ExtractorResult> ExtractAsync(IReadOnlyList<ExtractionDocument> documents, ExtractionContext context, CancellationToken cancellationToken = default);
}

/// <param name="Name">Name of the provider ("Keiner", "Claude API", "Ollama").</param>
/// <param name="Model">Configured model, if any.</param>
/// <param name="Enabled">A provider other than "None" is configured.</param>
/// <param name="Ready">Enabled and usable (opt-in given, key present).</param>
/// <param name="SendsToThirdParty">The documents leave the own server (cloud API).</param>
/// <param name="Notice">Data protection notice for users (German).</param>
/// <param name="Problem">Why it is not ready (German), otherwise <c>null</c>.</param>
public sealed record ExtractorInfo(string Name, string? Model, bool Enabled, bool Ready, bool SendsToThirdParty, string Notice, string? Problem);

/// <summary>One page of the document. The content is held in memory (limited by the file size limit).</summary>
public sealed record ExtractionDocument(string FileName, string MimeType, ReadOnlyMemory<byte> Content);

/// <summary>Known values that help the model (cost type catalog is part of the prompt).</summary>
public sealed record ExtractionContext(DateOnly Today);

/// <summary>Raw suggestion plus usage for the cost display.</summary>
public sealed record ExtractorResult(ReceiptSuggestion Suggestion, ExtractionUsage Usage);

/// <summary>Tokens used for one extraction (zero if the provider reports nothing).</summary>
public sealed record ExtractionUsage(int InputTokens, int OutputTokens)
{
    public static ExtractionUsage None { get; } = new(0, 0);
}

/// <summary>Extraction failed; <see cref="Exception.Message"/> is phrased for users and never contains secrets.</summary>
public sealed class ExtractionException : Exception
{
    public ExtractionException()
    {
    }

    public ExtractionException(string message)
        : base(message)
    {
    }

    public ExtractionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
