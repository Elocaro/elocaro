using MietHahn.Domain.Receipts;

namespace MietHahn.Application.Receipts;

/// <summary>
/// Use case "read a suggestion from the uploaded files": loads the files, calls the configured provider, checks the
/// plausibility, applies learned supplier rules and records the transfer in the access log.
/// Nothing is saved; the user confirms the suggestion in the receipt form.
/// </summary>
public interface IReceiptExtraction
{
    ExtractorInfo Info { get; }

    Task<ExtractionOutcome> SuggestAsync(IReadOnlyList<Guid> fileIds, CancellationToken cancellationToken = default);
}

/// <summary>Result of <see cref="IReceiptExtraction.SuggestAsync"/>; either a suggestion or an error message.</summary>
/// <param name="SupplierId">Contact whose name matches the supplier read from the document, if there is exactly one.</param>
public sealed record ExtractionOutcome(ReceiptSuggestion? Suggestion, ExtractionUsage Usage, string? Error, Guid? SupplierId = null)
{
    public static ExtractionOutcome Failed(string error) => new(null, ExtractionUsage.None, error);
}
