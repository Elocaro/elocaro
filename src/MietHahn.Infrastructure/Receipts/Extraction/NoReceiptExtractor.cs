using MietHahn.Application.Receipts;

namespace MietHahn.Infrastructure.Receipts.Extraction;

/// <summary>Provider "None": receipts are entered by hand.</summary>
internal sealed class NoReceiptExtractor : IReceiptExtractor
{
    public ExtractorInfo Info { get; } = new(
        "Keiner", null, Enabled: false, Ready: false, SendsToThirdParty: false,
        "Die automatische Belegauslesung ist ausgeschaltet. Belege werden von Hand erfasst, es werden keine Dokumente an Dritte übermittelt.",
        "Die Belegauslesung ist nicht aktiviert.");

    public Task<ExtractorResult> ExtractAsync(IReadOnlyList<ExtractionDocument> documents, ExtractionContext context, CancellationToken cancellationToken = default) =>
        throw new ExtractionException("Die Belegauslesung ist nicht aktiviert.");
}
